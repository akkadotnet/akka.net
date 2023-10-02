//-----------------------------------------------------------------------
// <copyright file="ExternalShardAllocationStrategySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding.External;
using Akka.Cluster.Sharding.Internal;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.DistributedData;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests.External
{
    /// <summary>
    /// Covers the interaction between the shard and the remember entities store
    /// </summary>
    public class ExternalShardAllocationStrategySpec : AkkaSpec
    {
        private readonly TestProbe requester;

        public ExternalShardAllocationStrategySpec(ITestOutputHelper helper) : base(SpecConfig, helper)
        {
            requester = CreateTestProbe();
        }

        private static Config SpecConfig =>
            ConfigurationFactory.ParseString(@"
                akka.loglevel=DEBUG
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
            ")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(ClusterSharding.DefaultConfig())
            .WithFallback(DistributedData.DistributedData.DefaultConfig());

        private class ExternalShardAllocationStrategyTest : ExternalShardAllocationStrategy
        {
            private readonly IActorRef probe;

            public ExternalShardAllocationStrategyTest(ActorSystem system, string typeName, IActorRef probe)
                : base(system, typeName)
            {
                this.probe = probe;
            }

            protected override TimeSpan Timeout => TimeSpan.FromMilliseconds(250);

            protected override IActorRef CreateShardStateActor()
            {
                return probe;
            }
        }

        private (ExternalShardAllocationStrategy, TestProbe) CreateStrategy()
        {
            var probe = CreateTestProbe();
            var strategy = new ExternalShardAllocationStrategyTest(Sys, "type", probe);
            strategy.Start();
            return (strategy, probe);
        }

        [Fact]
        public async Task ExternalShardAllocationClient_must_default_to_no_locations_if_sharding_never_started()
        {
            var result = await ExternalShardAllocation.Get(Sys)
                .ClientFor("not found")
                .ShardLocations()
                .ShouldCompleteWithin(RemainingOrDefault);
            result.Locations.Should().BeEmpty();
        }

        [Fact]
        public async Task ExternalShardAllocation_allocate_must_default_to_requester_if_query_times_out()
        {
            var (strat, _) = CreateStrategy();
            var result = await strat.AllocateShard(requester.Ref, "shard-1", ImmutableDictionary<IActorRef, IImmutableList<string>>.Empty)
                .ShouldCompleteWithin(RemainingOrDefault);
            result.Should().Be(requester.Ref);
        }

        [Fact]
        public async Task ExternalShardAllocation_allocate_must_default_to_requester_if_no_allocation()
        {
            var (strat, probe) = CreateStrategy();
            var allocation = strat.AllocateShard(requester.Ref, "shard-1", ImmutableDictionary<IActorRef, IImmutableList<string>>.Empty)
                .ShouldCompleteWithin(RemainingOrDefault);
            probe.ExpectMsg(new ExternalShardAllocationStrategy.GetShardLocation("shard-1"));
            probe.Reply(new ExternalShardAllocationStrategy.GetShardLocationResponse(null));
            (await allocation).Should().Be(requester.Ref);
        }

        [Fact]
        public async Task ExternalShardAllocation_rebalance_must_default_to_no_rebalance_if_query_times_out()
        {
            var (strat, probe) = CreateStrategy();
            var rebalance = strat.Rebalance(ImmutableDictionary<IActorRef, IImmutableList<string>>.Empty, ImmutableHashSet<string>.Empty)
                .ShouldCompleteWithin(RemainingOrDefault);
            probe.ExpectMsg<ExternalShardAllocationStrategy.GetShardLocations>();
            (await rebalance).Should().BeEmpty();
        }
    }
}
