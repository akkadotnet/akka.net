//-----------------------------------------------------------------------
// <copyright file="ClusterShardingSingleShardPerEntitySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests;

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

    private Task JoinAsync(RoleName from, RoleName to)
    {
        return JoinAsync(
            from,
            to,
            () => StartSharding(
                Sys,
                typeName: "Entity",
                entityProps: Props.Create(() => new ShardedEntity())));
    }

    private async Task JoinAndAllocate(RoleName node, int entityId)
    {
        await WithinAsync(TimeSpan.FromSeconds(10), async () =>
        {
            await JoinAsync(node, Config.First);
            await RunOnAsync(async () =>
            {
                _region.Value.Tell(entityId);

                await ExpectMsgAsync(entityId);

                LastSender.Path.Should().Be(_region.Value.Path / $"{entityId}" / $"{entityId}");
            }, node);
        });
        await EnterBarrierAsync($"started-{entityId}");
    }


    #endregion

    [MultiNodeFact]
    public async Task Cluster_sharding_with_single_shard_per_entity_specs()
    {
        await Cluster_sharding_with_single_shard_per_entity_must_use_specified_region();
    }

    private async Task Cluster_sharding_with_single_shard_per_entity_must_use_specified_region()
    {
        await JoinAndAllocate(Config.First, 1);
        await JoinAndAllocate(Config.Second, 2);
        await JoinAndAllocate(Config.Third, 3);
        await JoinAndAllocate(Config.Fourth, 4);
        await JoinAndAllocate(Config.Fifth, 5);

        await RunOnAsync(async () =>
        {
            // coordinator is on 'first', blackhole 3 other means that it can't update with WriteMajority
            await TestConductor.BlackholeAsync(Config.First, Config.Third, ThrottleTransportAdapter.Direction.Both);
            await TestConductor.BlackholeAsync(Config.First, Config.Fourth, ThrottleTransportAdapter.Direction.Both);
            await TestConductor.BlackholeAsync(Config.First, Config.Fifth, ThrottleTransportAdapter.Direction.Both);

            // shard 6 not allocated yet and due to the blackhole it will not be completed
            _region.Value.Tell(6);

            // shard 1 location is know by 'first' region, not involving coordinator
            _region.Value.Tell(1);
            await ExpectMsgAsync(1);

            // shard 2 location not known at 'first' region yet, but coordinator is on 'first' and should
            // be able to answer GetShardHome even though previous request for shard 4 has not completed yet
            _region.Value.Tell(2);
            await ExpectMsgAsync(2);
            LastSender.Path.Should().Be(await NodeAsync(Config.Second) / "system" / "sharding" / "Entity" / "2" / "2");

            await TestConductor.PassThroughAsync(Config.First, Config.Third, ThrottleTransportAdapter.Direction.Both);
            await TestConductor.PassThroughAsync(Config.First, Config.Fourth, ThrottleTransportAdapter.Direction.Both);
            await TestConductor.PassThroughAsync(Config.First, Config.Fifth, ThrottleTransportAdapter.Direction.Both);
            await ExpectMsgAsync(6, TimeSpan.FromSeconds(10));
        }, Config.First);

        await EnterBarrierAsync("after-1");
    }
}