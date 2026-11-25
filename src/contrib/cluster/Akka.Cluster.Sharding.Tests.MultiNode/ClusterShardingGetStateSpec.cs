//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGetStateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingGetStateSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName Controller { get; }
        public RoleName First { get; }
        public RoleName Second { get; }

        public ClusterShardingGetStateSpecConfig()
            : base(loglevel: "DEBUG", additionalConfig: @"
            akka.cluster.sharding {
                coordinator-failure-backoff = 3s
                shard-failure-backoff = 3s
            }
            ")
        {
            Controller = Role("controller");
            First = Role("first");
            Second = Role("second");

            NodeConfig(new RoleName[] { First, Second }, new Config[] {
                ConfigurationFactory.ParseString(@"akka.cluster.roles=[""shard""]")
            });
        }
    }

    public class ClusterShardingGetStateSpec : MultiNodeClusterShardingSpec<ClusterShardingGetStateSpecConfig>
    {
        #region setup

        private const int NumberOfShards = 2;
        private const string ShardTypeName = "Ping";

        private sealed class MessageExtractor: IMessageExtractor
        {
            public string? EntityId(object message)
                => message switch
                {
                    PingPongActor.Ping p => p.Id.ToString(),
                    _ => null
                };

            public object EntityMessage(object message)
                => message;

            public string? ShardId(object message)
                => message switch
                {
                    PingPongActor.Ping p => (p.Id % NumberOfShards).ToString(),
                    _ => null
                };

            public string ShardId(string entityId, object? messageHint = null)
                => (int.Parse(entityId) % NumberOfShards).ToString();
        }

        public ClusterShardingGetStateSpec()
            : this(new ClusterShardingGetStateSpecConfig(), typeof(ClusterShardingGetStateSpec))
        {
        }

        protected ClusterShardingGetStateSpec(ClusterShardingGetStateSpecConfig config, Type type)
            : base(config, type)
        {
        }

        #endregion

        [MultiNodeFact]
        public async Task Inspecting_cluster_sharding_state_specs()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await StartShardingOnClusterAsync(cancellationToken);
            var hostingRegions = new[]
            {
                (await NodeAsync(Config.First, cancellationToken)).Address,
                (await NodeAsync(Config.Second, cancellationToken)).Address
            };
            await AssertEmptyShardStateAsync(hostingRegions, cancellationToken);
            await StartEntitiesAsync(cancellationToken);
            await AssertPopulatedShardStateAsync(hostingRegions, cancellationToken);
        }

        private async Task StartShardingOnClusterAsync(CancellationToken cancellationToken)
        {
            await JoinAsync(Config.Controller, Config.Controller, cancellationToken: cancellationToken);
            await JoinAsync(Config.First, Config.Controller, cancellationToken: cancellationToken);
            await JoinAsync(Config.Second, Config.Controller, cancellationToken: cancellationToken);

            await AwaitAssertAsync(async () =>
            {
                Cluster.Get(Sys).SendCurrentClusterState(TestActor);
                var state = await ExpectMsgAsync<ClusterEvent.CurrentClusterState>(
                    TimeSpan.FromSeconds(1), cancellationToken: cancellationToken);
                state.Members.Count.Should().Be(3);
            }, cancellationToken: cancellationToken);

            RunOn(() =>
            {
                StartProxy(
                    Sys,
                    typeName: ShardTypeName,
                    role: "shard",
                    messageExtractor: new MessageExtractor());
            }, Config.Controller);

            RunOn(() =>
            {
                StartSharding(
                    Sys,
                    typeName: ShardTypeName,
                    entityProps: Props.Create(() => new PingPongActor()),
                    settings: Settings.Value.WithRole("shard"),
                    messageExtractor: new MessageExtractor());
            }, Config.First, Config.Second);

            await EnterBarrierAsync(cancellationToken, "sharding started");
        }

        private async Task AssertEmptyShardStateAsync(Address[] hostingRegions, CancellationToken cancellationToken)
        {
            // The start barrier only waits for the region actors to be created.
            await AwaitAssertAsync(() => AssertRegisteredRegionsAsync(hostingRegions, cancellationToken),
                TimeSpan.FromSeconds(20), cancellationToken: cancellationToken);

            var states = await GetShardRegionStatesAsync(hostingRegions, cancellationToken);
            foreach (var state in states)
            {
                state.Failed.Should().BeEmpty();
                state.Shards.Should().BeEmpty();
            }

            await EnterBarrierAsync(cancellationToken, "empty sharding");
        }

        private async Task StartEntitiesAsync(CancellationToken cancellationToken)
        {
            await RunOnAsync(async () =>
            {
                var region = ClusterSharding.Get(Sys).ShardRegion(ShardTypeName);
                await Task.WhenAll(Enumerable.Range(1, 4).Select(id => region.Ask<PingPongActor.Pong>(
                    new PingPongActor.Ping(id), TimeSpan.FromSeconds(10), cancellationToken)));
            }, Config.Controller);

            await EnterBarrierAsync(cancellationToken, "sharded actors started");
        }

        private async Task AssertPopulatedShardStateAsync(Address[] hostingRegions, CancellationToken cancellationToken)
        {
            await AwaitAssertAsync(async () =>
            {
                await AssertRegisteredRegionsAsync(hostingRegions, cancellationToken);
                var states = await GetShardRegionStatesAsync(hostingRegions, cancellationToken);
                states.Should().OnlyContain(state => state.Failed.Count == 0);
                var allEntityIds = states.SelectMany(state => state.Shards)
                    .SelectMany(shard => shard.EntityIds).ToImmutableHashSet();
                allEntityIds.Should().BeEquivalentTo(new[] { "1", "2", "3", "4" });
            }, TimeSpan.FromSeconds(10), cancellationToken: cancellationToken);

            await EnterBarrierAsync(cancellationToken, "done");
        }

        private async Task AssertRegisteredRegionsAsync(Address[] hostingRegions, CancellationToken cancellationToken)
        {
            var region = ClusterSharding.Get(Sys).ShardRegion(ShardTypeName);
            var current = await region.Ask<CurrentRegions>(
                GetCurrentRegions.Instance, TimeSpan.FromSeconds(1), cancellationToken);
            current.Regions.Should().BeEquivalentTo(hostingRegions);
        }

        private Task<CurrentShardRegionState[]> GetShardRegionStatesAsync(
            Address[] hostingRegions, CancellationToken cancellationToken)
            => Task.WhenAll(hostingRegions.Select(address =>
            {
                var path = new RootActorPath(address) / "system" / "sharding" / ShardTypeName;
                return Sys.ActorSelection(path).Ask<CurrentShardRegionState>(
                    GetShardRegionState.Instance, TimeSpan.FromSeconds(5), cancellationToken);
            }));
    }
}
