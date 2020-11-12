//-----------------------------------------------------------------------
// <copyright file="ClusterShardingSingleShardPerEntitySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests
{
    public class ClusterShardingSingleShardPerEntitySpecConfig : MultiNodeClusterShardingConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }
        public RoleName Fifth { get; }

        public Config R1Config { get; }
        public Config R2Config { get; }

        public ClusterShardingSingleShardPerEntitySpecConfig()
            : base(loglevel: "DEBUG", additionalConfig: @"
                akka.cluster.sharding.updating-state-timeout = 1s
            ")
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");

            TestTransport = true;
        }
    }

    public class ClusterShardingSingleShardPerEntitySpec : MultiNodeClusterShardingSpec<ClusterShardingSingleShardPerEntitySpecConfig>
    {
        #region setup

        private readonly Lazy<IActorRef> _region;

        public ClusterShardingSingleShardPerEntitySpec()
            : this(new ClusterShardingSingleShardPerEntitySpecConfig(), typeof(ClusterShardingSingleShardPerEntitySpec))
        {
        }

        protected ClusterShardingSingleShardPerEntitySpec(ClusterShardingSingleShardPerEntitySpecConfig config, Type type)
            : base(config, type)
        {
            _region = new Lazy<IActorRef>(() => ClusterSharding.Get(Sys).ShardRegion("Entity"));
        }

        private void Join(RoleName from, RoleName to)
        {
            Join(
                from,
                to,
                () => StartSharding(
                    Sys,
                    typeName: "Entity",
                    entityProps: Props.Create(() => new ShardedEntity()),
                    extractEntityId: IntExtractEntityId,
                    extractShardId: IntExtractShardId));
        }

        private void JoinAndAllocate(RoleName node, int entityId)
        {
            Within(TimeSpan.FromSeconds(10), () =>
            {
                Join(node, config.First);
                RunOn(() =>
                {
                    _region.Value.Tell(entityId);

                    ExpectMsg(entityId);

                    LastSender.Path.Should().Be(_region.Value.Path / $"{entityId}" / $"{entityId}");
                }, node);
            });
            EnterBarrier($"started-{entityId}");
        }


        #endregion

        [MultiNodeFact]
        public void Cluster_sharding_with_single_shard_per_entity_specs()
        {
            Cluster_sharding_with_single_shard_per_entity_must_use_specified_region();
        }

        private void Cluster_sharding_with_single_shard_per_entity_must_use_specified_region()
        {
            JoinAndAllocate(config.First, 1);
            JoinAndAllocate(config.Second, 2);
            JoinAndAllocate(config.Third, 3);
            JoinAndAllocate(config.Fourth, 4);
            JoinAndAllocate(config.Fifth, 5);

            RunOn(() =>
            {
                // coordinator is on 'first', blackhole 3 other means that it can't update with WriteMajority
                TestConductor.Blackhole(config.First, config.Third, ThrottleTransportAdapter.Direction.Both).Wait();
                TestConductor.Blackhole(config.First, config.Fourth, ThrottleTransportAdapter.Direction.Both).Wait();
                TestConductor.Blackhole(config.First, config.Fifth, ThrottleTransportAdapter.Direction.Both).Wait();

                // shard 6 not allocated yet and due to the blackhole it will not be completed
                _region.Value.Tell(6);

                // shard 1 location is know by 'first' region, not involving coordinator
                _region.Value.Tell(1);
                ExpectMsg(1);

                // shard 2 location not known at 'first' region yet, but coordinator is on 'first' and should
                // be able to answer GetShardHome even though previous request for shard 4 has not completed yet
                _region.Value.Tell(2);
                ExpectMsg(2);
                LastSender.Path.Should().Be(Node(config.Second) / "system" / "sharding" / "Entity" / "2" / "2");

                TestConductor.PassThrough(config.First, config.Third, ThrottleTransportAdapter.Direction.Both).Wait();
                TestConductor.PassThrough(config.First, config.Fourth, ThrottleTransportAdapter.Direction.Both).Wait();
                TestConductor.PassThrough(config.First, config.Fifth, ThrottleTransportAdapter.Direction.Both).Wait();
                ExpectMsg(6, TimeSpan.FromSeconds(10));
            }, config.First);

            EnterBarrier("after-1");
        }
    }
}
