//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGetStatsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    public class ClusterShardingGetStatsSpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName Controller { get; }
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public ClusterShardingGetStatsSpecConfig()
            : base(loglevel: "DEBUG", additionalConfig: @"
            akka.log-dead-letters-during-shutdown = off
            akka.cluster.sharding.updating-state-timeout = 2s
            akka.cluster.sharding.waiting-for-state-timeout = 2s
            ")
        {
            Controller = Role("controller");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            NodeConfig(new RoleName[] { First, Second, Third }, new Config[] {
                ConfigurationFactory.ParseString(@"akka.cluster.roles=[""shard""]")
            });
        }
    }

    public class ClusterShardingGetStatsSpec : MultiNodeClusterShardingSpec<ClusterShardingGetStatsSpecConfig>
    {
        #region setup

        private const int NumberOfShards = 3;
        private const string ShardTypeName = "Ping";

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

        private readonly Lazy<IActorRef> _region;

        public ClusterShardingGetStatsSpec()
            : this(new ClusterShardingGetStatsSpecConfig(), typeof(ClusterShardingGetStatsSpec))
        {
        }

        protected ClusterShardingGetStatsSpec(ClusterShardingGetStatsSpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion(ShardTypeName));
        }

        private IActorRef StartShard()
        {
            return StartSharding(
                Sys,
                typeName: ShardTypeName,
                entityProps: Props.Create(() => new PingPongActor()),
                settings: settings.Value.WithRole("shard"),
                extractEntityId: extractEntityId,
                extractShardId: extractShardId);
        }

        #endregion

        [MultiNodeFact]
        public void Inspecting_cluster_sharding_state_specs()
        {
            Inspecting_cluster_sharding_state_must_join_cluster();
            Inspecting_cluster_sharding_state_must_return_empty_state_when_no_sharded_actors_has_started();
            Inspecting_cluster_sharding_state_must_trigger_sharded_actors();
            Inspecting_cluster_sharding_state_must_get_shard_state();
            Inspecting_cluster_sharding_state_must_return_stats_after_a_node_leaves();
        }

        private void Inspecting_cluster_sharding_state_must_join_cluster()
        {
            Join(config.Controller, config.Controller);
            Join(config.First, config.Controller);
            Join(config.Second, config.Controller);
            Join(config.Third, config.Controller);

            // make sure all nodes are up
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    Cluster.Get(Sys).State.Members.Count(i => i.Status == MemberStatus.Up).Should().Be(4);
                });
            });

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
                StartShard();
            }, config.First, config.Second, config.Third);

            EnterBarrier("sharding started");
        }

        private void Inspecting_cluster_sharding_state_must_return_empty_state_when_no_sharded_actors_has_started()
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe();
                    _region.Value.Tell(new GetClusterShardingStats(Dilated(TimeSpan.FromSeconds(10))), probe.Ref);
                    var shardStats = probe.ExpectMsg<ClusterShardingStats>();
                    shardStats.Regions.Count.Should().Be(3);
                    shardStats.Regions.Values.Sum(i => i.Stats.Count).Should().Be(0);
                    shardStats.Regions.Keys.Should().OnlyContain(i => i.HasGlobalScope);
                    shardStats.Regions.Values.Should().OnlyContain(i => i.Failed.Count == 0);
                });
            });

            EnterBarrier("empty sharding");
        }

        private void Inspecting_cluster_sharding_state_must_trigger_sharded_actors()
        {
            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(10), () =>
                {
                    AwaitAssert(() =>
                    {
                        var pingProbe = CreateTestProbe();
                        // trigger starting of 2 entities on first and second node
                        // but leave third node without entities
                        foreach (var n in new int[] { 1, 2, 4, 6 })
                        {
                            _region.Value.Tell(new PingPongActor.Ping(n), pingProbe.Ref);
                        }
                        pingProbe.ReceiveWhile(null, m => (PingPongActor.Pong)m, 4);
                    });
                });
            }, config.Controller);

            EnterBarrier("sharded actors started");
        }

        private void Inspecting_cluster_sharding_state_must_get_shard_state()
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    var probe = CreateTestProbe();
                    var region = ClusterSharding.Get(Sys).ShardRegion(ShardTypeName);
                    region.Tell(new GetClusterShardingStats(Dilated(TimeSpan.FromSeconds(10))), probe.Ref);
                    var regions = probe.ExpectMsg<ClusterShardingStats>().Regions;
                    regions.Count.Should().Be(3);
                    regions.Values.SelectMany(i => i.Stats.Values).Sum().Should().Be(4);
                    regions.Values.Should().OnlyContain(i => i.Failed.Count == 0);
                    regions.Keys.Should().OnlyContain(i => i.HasGlobalScope);
                });
            });

            EnterBarrier("got shard state");
        }

        private void Inspecting_cluster_sharding_state_must_return_stats_after_a_node_leaves()
        {
            RunOn(() =>
            {
                Cluster.Get(Sys).Leave(Node(config.Third).Address);
            }, config.Controller);

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(30), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.Get(Sys).State.Members.Count.Should().Be(3);
                    });
                });
            }, config.First, config.Second);

            EnterBarrier("third node removed");
            Sys.Log.Info("third node removed");

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(10), () =>
                {
                    AwaitAssert(() =>
                    {
                        var pingProbe = CreateTestProbe();
                        // make sure we have the 4 entities still alive across the fewer nodes
                        foreach (var n in new int[] { 1, 2, 4, 6 })
                        {
                            _region.Value.Tell(new PingPongActor.Ping(n), pingProbe.Ref);
                        }
                        pingProbe.ReceiveWhile(null, m => (PingPongActor.Pong)m, 4);
                    });
                });
            }, config.Controller);

            EnterBarrier("shards revived");

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(20), () =>
                {
                    AwaitAssert(() =>
                    {
                        var probe = CreateTestProbe();
                        _region.Value.Tell(new GetClusterShardingStats(Dilated(TimeSpan.FromSeconds(20))), probe.Ref);
                        var regions = probe.ExpectMsg<ClusterShardingStats>().Regions;
                        regions.Count.Should().Be(2);
                        regions.Values.SelectMany(i => i.Stats.Values).Sum().Should().Be(4);
                    });
                });
            }, config.Controller);

            EnterBarrier("done");
        }
    }
}
