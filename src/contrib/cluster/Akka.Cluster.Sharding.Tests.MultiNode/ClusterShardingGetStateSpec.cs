//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGetStateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Util;
using FluentAssertions;

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
        public void Inspecting_cluster_sharding_state_specs()
        {
            Inspecting_cluster_sharding_state_must_join_cluster();
            Inspecting_cluster_sharding_state_must_return_empty_state_when_no_sharded_actors_has_started();
            Inspecting_cluster_sharding_state_must_trigger_sharded_actors();
            Inspecting_cluster_sharding_state_must_get_shard_state();
        }

        private void Inspecting_cluster_sharding_state_must_join_cluster()
        {
            Join(config.Controller, config.Controller);
            Join(config.First, config.Controller);
            Join(config.Second, config.Controller);

            // make sure all nodes are up
            AwaitAssert(() =>
            {
                Cluster.Get(Sys).SendCurrentClusterState(TestActor);
                ExpectMsg<ClusterEvent.CurrentClusterState>().Members.Count.Should().Be(3);
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
                StartSharding(
                    Sys,
                    typeName: ShardTypeName,
                    entityProps: Props.Create(() => new PingPongActor()),
                    settings: settings.Value.WithRole("shard"),
                    extractEntityId: extractEntityId,
                    extractShardId: extractShardId);
            }, config.First, config.Second);

            EnterBarrier("sharding started");
        }

        private void Inspecting_cluster_sharding_state_must_return_empty_state_when_no_sharded_actors_has_started()
        {
            AwaitAssert(() =>
            {
                var probe = CreateTestProbe();
                var region = ClusterSharding.Get(Sys).ShardRegion(ShardTypeName);
                region.Tell(GetCurrentRegions.Instance, probe.Ref);
                probe.ExpectMsg<CurrentRegions>().Regions.Count.Should().Be(0);
            });

            EnterBarrier("empty sharding");
        }

        private void Inspecting_cluster_sharding_state_must_trigger_sharded_actors()
        {
            RunOn(() =>
            {
                var region = ClusterSharding.Get(Sys).ShardRegion(ShardTypeName);

                Within(TimeSpan.FromSeconds(10), () =>
                {
                    AwaitAssert(() =>
                    {
                        var pingProbe = CreateTestProbe();
                        // trigger starting of 4 entities
                        foreach (var n in Enumerable.Range(1, 4))
                        {
                            region.Tell(new PingPongActor.Ping(n), pingProbe.Ref);
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
                    region.Tell(GetCurrentRegions.Instance, probe.Ref);
                    var regions = probe.ExpectMsg<CurrentRegions>().Regions;
                    regions.Count.Should().Be(2);

                    foreach (var r in regions)
                    {
                        var path = new RootActorPath(r) / "system" / "sharding" / ShardTypeName;
                        Sys.ActorSelection(path).Tell(GetShardRegionState.Instance, probe.Ref);
                    }

                    var states = probe.ReceiveWhile(null, m => (CurrentShardRegionState)m, regions.Count);
                    var allEntityIds = states.SelectMany(i => i.Shards).SelectMany(j => j.EntityIds).ToImmutableHashSet();
                    allEntityIds.Should().BeEquivalentTo(new string[] { "1", "2", "3", "4" });
                });
            });

            EnterBarrier("done");
        }
    }
}
