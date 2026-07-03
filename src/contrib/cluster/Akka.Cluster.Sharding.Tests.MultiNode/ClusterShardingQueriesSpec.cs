//-----------------------------------------------------------------------
// <copyright file="ClusterShardingQueriesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingQueriesSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName Controller { get; }
        public RoleName Busy { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterShardingQueriesSpecConfig()
            : base(loglevel: "DEBUG", additionalConfig: @"
            akka.cluster.sharding.rebalance-interval = 120s #disable rebalance
            akka.cluster.min-nr-of-members = 3
            ")
        {
            Controller = Role("controller");
            Busy = Role("busy");
            Second = Role("second");
            Third = Role("third");

            var shardRoles = ConfigurationFactory.ParseString(@"akka.cluster.roles=[""shard""]");

            NodeConfig(new RoleName[] { Busy }, new Config[] {
                ConfigurationFactory.ParseString(@"akka.cluster.sharding.shard-region-query-timeout = 0ms")
                    .WithFallback(shardRoles)
            });

            NodeConfig(new RoleName[] { Second, Third }, new Config[] {
                shardRoles
            });
        }
    }

    public class ClusterShardingQueriesSpec : MultiNodeClusterShardingSpec<ClusterShardingQueriesSpecConfig>
    {
        #region setup

        private sealed class MessageExtractor: IMessageExtractor
        {
            public string EntityId(object message)
                => message switch
                {
                    PingPongActor.Ping p => p.Id.ToString(),
                    _ => null
                };

            public object EntityMessage(object message)
                => message;

            public string ShardId(object message)
                => message switch
                {
                    PingPongActor.Ping p => (p.Id % NumberOfShards).ToString(),
                    _ => null
                };

            public string ShardId(string entityId, object messageHint = null)
                => (int.Parse(entityId) % NumberOfShards).ToString();
        }

        private const int NumberOfShards = 6;
        private const string ShardTypeName = "DatatypeA";

        private readonly Lazy<IActorRef> _region;

        public ClusterShardingQueriesSpec()
            : this(new ClusterShardingQueriesSpecConfig(), typeof(ClusterShardingQueriesSpec))
        {
        }

        protected ClusterShardingQueriesSpec(ClusterShardingQueriesSpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion(ShardTypeName));
        }

        #endregion

        [MultiNodeFact]
        public async Task Querying_cluster_sharding_specs()
        {
            await Querying_cluster_sharding_must_join_cluster_initialize_sharding();
            await Querying_cluster_sharding_must_trigger_sharded_actors();
            await Querying_cluster_sharding_must_return_shard_stats_of_cluster_sharding_regions_if_one_or_more_shards_timeout_versus_all_as_empty();
            await Querying_cluster_sharding_must_return_shard_state_of_sharding_regions_if_one_or_more_shards_timeout_versus_all_as_empty();
        }

        private async Task Querying_cluster_sharding_must_join_cluster_initialize_sharding()
        {
            await AwaitClusterUpAsync(default, Config.Controller, Config.Busy, Config.Second, Config.Third);

            await RunOnAsync(() =>
            {
                StartProxy(
                    Sys,
                    typeName: ShardTypeName,
                    role: "shard",
                    messageExtractor: new MessageExtractor());
                return Task.CompletedTask;
            }, Config.Controller);

            await RunOnAsync(() =>
            {
                StartSharding(
                    Sys,
                    typeName: ShardTypeName,
                    entityProps: Props.Create(() => new PingPongActor()),
                    settings: Settings.Value.WithRole("shard"),
                    messageExtractor: new MessageExtractor());
                return Task.CompletedTask;
            }, Config.Busy, Config.Second, Config.Third);

            await EnterBarrierAsync("sharding started");
        }

        private async Task Querying_cluster_sharding_must_trigger_sharded_actors()
        {
            await RunOnAsync(async () =>
            {
                await WithinAsync(TimeSpan.FromSeconds(10), async () =>
                {
                    await AwaitAssertAsync(async () =>
                    {
                        var pingProbe = CreateTestProbe();
                        foreach (var n in Enumerable.Range(0, 20))
                        {
                            _region.Value.Tell(new PingPongActor.Ping(n), pingProbe.Ref);
                        }

                        await foreach (var _ in pingProbe.ReceiveWhileAsync(null, m => (PingPongActor.Pong)m, 20))
                        {
                        }
                    });
                });
            }, Config.Controller);
            await EnterBarrierAsync("sharded actors started");
        }

        private async Task Querying_cluster_sharding_must_return_shard_stats_of_cluster_sharding_regions_if_one_or_more_shards_timeout_versus_all_as_empty()
        {
            await RunOnAsync(async () =>
            {
                // The GetClusterShardingStats read is a point-in-time snapshot that races
                // sharding's internal shard-region-query-timeout (3s on Second/Third). Under
                // CI load a Second/Third Shard actor can momentarily miss that timeout and be
                // reported in Failed instead of Stats, transiently undercounting the Stats sum
                // to 3. The product is correct by design (partial results are expected), so we
                // re-issue the query on a fresh probe per attempt until the deterministic steady
                // state converges. Busy's 0ms shard-region-query-timeout keeps its 2 shards in
                // Failed (the feature under test), so Stats sum == 4 and Failed sum ==
                // NumberOfShards / regions.Count.
                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    var region = ClusterSharding.Get(Sys).ShardRegion(ShardTypeName);
                    region.Tell(new GetClusterShardingStats(Dilated(TimeSpan.FromSeconds(10))), probe.Ref);
                    var regions = (await probe.ExpectMsgAsync<ClusterShardingStats>(Dilated(TimeSpan.FromSeconds(10)))).Regions;
                    regions.Count.Should().Be(3);
                    var timeouts = NumberOfShards / regions.Count;

                    // 3 regions, 2 shards per region; only Busy's 2 shards are unresponsive
                    // within its 0ms shard-region-query-timeout, so exactly `timeouts` shards
                    // report as Failed while the other 4 report Stats.
                    regions.Values.Select(i => i.Stats.Count).Sum().Should().Be(4);
                    regions.Values.Select(i => i.Failed.Count).Sum().Should().Be(timeouts);
                }, Dilated(TimeSpan.FromSeconds(30)), TimeSpan.FromSeconds(1));
            }, Config.Busy, Config.Second, Config.Third);
            await EnterBarrierAsync("received failed stats from timed out shards vs empty");
        }

        private async Task Querying_cluster_sharding_must_return_shard_state_of_sharding_regions_if_one_or_more_shards_timeout_versus_all_as_empty()
        {
            await RunOnAsync(async () =>
            {
                // Busy's 0ms shard-region-query-timeout deterministically fails both of its
                // shards, but GetShardRegionState is still a one-shot read; converge-then-assert
                // on a fresh probe per attempt for robustness under CI load.
                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    var region = ClusterSharding.Get(Sys).ShardRegion(ShardTypeName);
                    region.Tell(GetShardRegionState.Instance, probe.Ref);
                    var state = await probe.ExpectMsgAsync<CurrentShardRegionState>(Dilated(TimeSpan.FromSeconds(10)));
                    state.Shards.Should().BeEmpty();
                    state.Failed.Should().HaveCount(2);
                }, Dilated(TimeSpan.FromSeconds(30)), TimeSpan.FromSeconds(1));
            }, Config.Busy);
            await EnterBarrierAsync("query-timeout-on-busy-node");

            await RunOnAsync(async () =>
            {
                // The GetShardRegionState read on Second/Third races the same internal 3s
                // shard-region-query-timeout as the stats query above, so it can transiently
                // report a shard in Failed rather than Shards. Re-issue the query on a fresh
                // probe per attempt until the steady state (2 shards, none failed) converges.
                await AwaitAssertAsync(async () =>
                {
                    var probe = CreateTestProbe();
                    var region = ClusterSharding.Get(Sys).ShardRegion(ShardTypeName);
                    region.Tell(GetShardRegionState.Instance, probe.Ref);
                    var state = await probe.ExpectMsgAsync<CurrentShardRegionState>(Dilated(TimeSpan.FromSeconds(10)));
                    state.Shards.Should().HaveCount(2);
                    state.Failed.Should().BeEmpty();
                }, Dilated(TimeSpan.FromSeconds(30)), TimeSpan.FromSeconds(1));
            }, Config.Second, Config.Third);
            await EnterBarrierAsync("done");
        }
    }
}
