//-----------------------------------------------------------------------
// <copyright file="ClusterShardingQueriesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
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

        private ExtractEntityId extractEntityId = message =>
        {
            switch (message)
            {
                case PingPongActor.Ping msg:
                    return (msg.Id.ToString(), message);
            }
            return Option<(string, object)>.None;
        };

        private ExtractShardId extractShardId = message =>
        {
            switch (message)
            {
                case PingPongActor.Ping msg:
                    return (msg.Id % NumberOfShards).ToString();
            }
            return null;
        };

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
        public void Querying_cluster_sharding_specs()
        {
            Querying_cluster_sharding_must_join_cluster_initialize_sharding();
            Querying_cluster_sharding_must_trigger_sharded_actors();
            Querying_cluster_sharding_must_return_shard_stats_of_cluster_sharding_regions_if_one_or_more_shards_timeout_versus_all_as_empty();
            Querying_cluster_sharding_must_return_shard_state_of_sharding_regions_if_one_or_more_shards_timeout_versus_all_as_empty();
        }

        private void Querying_cluster_sharding_must_join_cluster_initialize_sharding()
        {
            AwaitClusterUp(config.Controller, config.Busy, config.Second, config.Third);

            RunOn(() =>
            {
                StartProxy(
                    Sys,
                    typeName: ShardTypeName,
                    role: "shard",
                    extractEntityId: extractEntityId,
                    extractShardId: extractShardId);
            }, config.Controller);

            RunOn(() =>
            {
                StartSharding(
                    Sys,
                    typeName: ShardTypeName,
                    entityProps: Props.Create(() => new PingPongActor()),
                    settings: settings.Value.WithRole("shard"),
                    extractEntityId: extractEntityId,
                    extractShardId: extractShardId);
            }, config.Busy, config.Second, config.Third);

            EnterBarrier("sharding started");
        }

        private void Querying_cluster_sharding_must_trigger_sharded_actors()
        {
            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(10), () =>
                {
                    AwaitAssert(() =>
                    {
                        var pingProbe = CreateTestProbe();
                        foreach (var n in Enumerable.Range(0, 20))
                        {
                            _region.Value.Tell(new PingPongActor.Ping(n), pingProbe.Ref);
                        }
                        pingProbe.ReceiveWhile(null, m => (PingPongActor.Pong)m, 20);
                    });
                });
            }, config.Controller);
            EnterBarrier("sharded actors started");
        }

        private void Querying_cluster_sharding_must_return_shard_stats_of_cluster_sharding_regions_if_one_or_more_shards_timeout_versus_all_as_empty()
        {
            RunOn(() =>
            {
                var probe = CreateTestProbe();
                var region = ClusterSharding.Get(Sys).ShardRegion(ShardTypeName);
                region.Tell(new GetClusterShardingStats(TimeSpan.FromSeconds(10)), probe.Ref);
                var regions = probe.ExpectMsg<ClusterShardingStats>().Regions;
                regions.Count.Should().Be(3);
                var timeouts = NumberOfShards / regions.Count;

                // 3 regions, 2 shards per region, all 2 shards/region were unresponsive
                // within shard-region-query-timeout, which only on first is 0ms
                regions.Values.Select(i => i.Stats.Count).Sum().Should().Be(4);
                regions.Values.Select(i => i.Failed.Count).Sum().Should().Be(timeouts);
            }, config.Busy, config.Second, config.Third);
            EnterBarrier("received failed stats from timed out shards vs empty");
        }

        private void Querying_cluster_sharding_must_return_shard_state_of_sharding_regions_if_one_or_more_shards_timeout_versus_all_as_empty()
        {
            RunOn(() =>
            {
                var probe = CreateTestProbe();
                var region = ClusterSharding.Get(Sys).ShardRegion(ShardTypeName);
                region.Tell(GetShardRegionState.Instance, probe.Ref);
                var state = probe.ExpectMsg<CurrentShardRegionState>();
                state.Shards.Should().BeEmpty();
                state.Failed.Should().HaveCount(2);
            }, config.Busy);
            EnterBarrier("query-timeout-on-busy-node");

            RunOn(() =>
            {
                var probe = CreateTestProbe();
                var region = ClusterSharding.Get(Sys).ShardRegion(ShardTypeName);

                region.Tell(GetShardRegionState.Instance, probe.Ref);
                var state = probe.ExpectMsg<CurrentShardRegionState>();
                state.Shards.Should().HaveCount(2);
                state.Failed.Should().BeEmpty();
            }, config.Second, config.Third);
            EnterBarrier("done");
        }
    }
}
