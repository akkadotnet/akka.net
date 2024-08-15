﻿// -----------------------------------------------------------------------
//  <copyright file="ClusterShardingSingleShardPerEntitySpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests;

public class ClusterShardingSingleShardPerEntitySpecConfig : MultiNodeClusterShardingConfig
{
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

    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }
    public RoleName Fourth { get; }
    public RoleName Fifth { get; }

    public Config R1Config { get; }
    public Config R2Config { get; }
}

public class
    ClusterShardingSingleShardPerEntitySpec : MultiNodeClusterShardingSpec<
    ClusterShardingSingleShardPerEntitySpecConfig>
{
    [MultiNodeFact]
    public void Cluster_sharding_with_single_shard_per_entity_specs()
    {
        Cluster_sharding_with_single_shard_per_entity_must_use_specified_region();
    }

    private void Cluster_sharding_with_single_shard_per_entity_must_use_specified_region()
    {
        JoinAndAllocate(Config.First, 1);
        JoinAndAllocate(Config.Second, 2);
        JoinAndAllocate(Config.Third, 3);
        JoinAndAllocate(Config.Fourth, 4);
        JoinAndAllocate(Config.Fifth, 5);

        RunOn(() =>
        {
            // coordinator is on 'first', blackhole 3 other means that it can't update with WriteMajority
            TestConductor.Blackhole(Config.First, Config.Third, ThrottleTransportAdapter.Direction.Both).Wait();
            TestConductor.Blackhole(Config.First, Config.Fourth, ThrottleTransportAdapter.Direction.Both).Wait();
            TestConductor.Blackhole(Config.First, Config.Fifth, ThrottleTransportAdapter.Direction.Both).Wait();

            // shard 6 not allocated yet and due to the blackhole it will not be completed
            _region.Value.Tell(6);

            // shard 1 location is know by 'first' region, not involving coordinator
            _region.Value.Tell(1);
            ExpectMsg(1);

            // shard 2 location not known at 'first' region yet, but coordinator is on 'first' and should
            // be able to answer GetShardHome even though previous request for shard 4 has not completed yet
            _region.Value.Tell(2);
            ExpectMsg(2);
            LastSender.Path.Should().Be(Node(Config.Second) / "system" / "sharding" / "Entity" / "2" / "2");

            TestConductor.PassThrough(Config.First, Config.Third, ThrottleTransportAdapter.Direction.Both).Wait();
            TestConductor.PassThrough(Config.First, Config.Fourth, ThrottleTransportAdapter.Direction.Both).Wait();
            TestConductor.PassThrough(Config.First, Config.Fifth, ThrottleTransportAdapter.Direction.Both).Wait();
            ExpectMsg(6, TimeSpan.FromSeconds(10));
        }, Config.First);

        EnterBarrier("after-1");
    }

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
                "Entity",
                entityProps: Props.Create(() => new ShardedEntity())));
    }

    private void JoinAndAllocate(RoleName node, int entityId)
    {
        Within(TimeSpan.FromSeconds(10), () =>
        {
            Join(node, Config.First);
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
}