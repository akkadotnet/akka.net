//-----------------------------------------------------------------------
// <copyright file="DownSelfQuarantinedByRemoteSpec.cs" company="Akka.NET Project">
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
using Akka.Remote;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode.SBR
{
    public class DownSelfQuarantinedByRemoteSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }

        public DownSelfQuarantinedByRemoteSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            // stable-after is far longer than the time we allow for the quarantined node to go away,
            // so the normal SBR decision can't be what removes it
            CommonConfig = ConfigurationFactory.ParseString(@"
                akka {
                    loglevel = INFO
                    cluster {
                        downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider""
                        split-brain-resolver.active-strategy = keep-majority
                        split-brain-resolver.stable-after = 30s
                    }
                    actor.provider = cluster
                    test.filter-leeway = 10s
                }")
                .WithFallback(MultiNodeLoggingConfig.LoggingConfig)
                .WithFallback(DebugConfig(false))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    /// <summary>
    /// Port of Pekko's <c>DowningWhenOtherHasQuarantinedThisActorSystemSpec</c>, see #9002.
    /// </summary>
    public class DownSelfQuarantinedByRemoteSpec : MultiNodeClusterSpec
    {
        private readonly DownSelfQuarantinedByRemoteSpecConfig _config;

        public DownSelfQuarantinedByRemoteSpec()
            : this(new DownSelfQuarantinedByRemoteSpecConfig())
        {
        }

        protected DownSelfQuarantinedByRemoteSpec(DownSelfQuarantinedByRemoteSpecConfig config)
            : base(config, typeof(DownSelfQuarantinedByRemoteSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public async Task DownSelfQuarantinedByRemoteSpecTests()
        {
            await Should_JoinCluster();
            await Should_DownSelf_When_QuarantinedByMember();
            await Should_NotDownAnyone_When_MemberLeavesGracefully();
        }

        private async Task Should_JoinCluster()
        {
            await AwaitClusterUpAsync(_config.First, _config.Second, _config.Third, _config.Fourth);
            await EnterBarrierAsync("after-1");
        }

        private async Task Should_DownSelf_When_QuarantinedByMember()
        {
            // dedicated probe, so Identify replies don't get mixed up with quarantine events
            var quarantineEvents = CreateTestProbe();
            await RunOnAsync(() =>
            {
                Sys.EventStream.Subscribe(quarantineEvents.Ref, typeof(ThisActorSystemQuarantinedEvent));
                return Task.CompletedTask;
            }, _config.First, _config.Second);
            await EnterBarrierAsync("subscribed");

            await RunOnAsync(() =>
            {
                var second = Cluster.State.Members.First(m => m.Address.Equals(GetAddress(_config.Second)));
                RARP.For(Sys).Provider.Transport.Quarantine(second.Address, second.UniqueAddress.Uid);
                return Task.CompletedTask;
            }, _config.First);
            await EnterBarrierAsync("quarantined");

            await RunOnAsync(async () =>
            {
                // shorter than stable-after, so this is not the ordinary SBR decision
                await WithinAsync(TimeSpan.FromSeconds(10), async () =>
                {
                    await AwaitAssertAsync(() =>
                    {
                        // cluster heartbeats alone don't trigger the refusal, so talk to first directly
                        Sys.ActorSelection(new RootActorPath(GetAddress(_config.First)) / "user")
                            .Tell(new Identify(null), ActorRefs.NoSender);
                        Cluster.IsTerminated.Should().BeTrue();
                    }, interval: TimeSpan.FromMilliseconds(200));
                });

                var quarantined = await quarantineEvents.ExpectMsgAsync<ThisActorSystemQuarantinedEvent>(TimeSpan.FromSeconds(5));
                quarantined.RemoteAddress.Should().Be(GetAddress(_config.First));
            }, _config.Second);
            await EnterBarrierAsync("second-shutdown");

            await RunOnAsync(async () =>
            {
                // first quarantined second, not the other way around
                await quarantineEvents.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
                Cluster.IsTerminated.Should().BeFalse();
            }, _config.First);

            await RunOnAsync(() =>
            {
                Cluster.IsTerminated.Should().BeFalse();
                return Task.CompletedTask;
            }, _config.First, _config.Third, _config.Fourth);

            await EnterBarrierAsync("after-2");
        }

        private async Task Should_NotDownAnyone_When_MemberLeavesGracefully()
        {
            var quarantineEvents = CreateTestProbe();
            await RunOnAsync(() =>
            {
                Sys.EventStream.Subscribe(quarantineEvents.Ref, typeof(ThisActorSystemQuarantinedEvent));
                return Task.CompletedTask;
            }, _config.First, _config.Fourth);
            await EnterBarrierAsync("subscribed-2");

            await RunOnAsync(async () =>
            {
                await Cluster.LeaveAsync();
            }, _config.Third);

            await RunOnAsync(async () =>
            {
                await WithinAsync(TimeSpan.FromSeconds(20), async () =>
                {
                    await AwaitAssertAsync(() =>
                    {
                        Cluster.State.Members.Select(m => m.Address).Should().NotContain(GetAddress(_config.Third));
                    });
                });

                // try to talk to the node that left: it must not make us down ourselves
                var sel = Sys.ActorSelection(new RootActorPath(GetAddress(_config.Third)) / "user");
                for (var i = 0; i < 10; i++)
                {
                    sel.Tell(new Identify(null), ActorRefs.NoSender);
                    await quarantineEvents.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                }

                Cluster.IsTerminated.Should().BeFalse();
            }, _config.First, _config.Fourth);

            await EnterBarrierAsync("after-3");
        }
    }
}
