// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;

namespace Akka.Cluster.Sharding.Tests;

internal static class ShardingWireCases
{
    public static readonly Address Owner = new("akka.tcp", "wire", "127.0.0.1", 2552);
    public static string Path(string name) => $"akka.tcp://wire@127.0.0.1:2552/user/{name}#42";

    public static (object Message, string Manifest, string Name)[] Create(Func<string, IActorRef> resolve)
    {
        var region = resolve("region");
        var proxy = resolve("proxy");
        var entities = ImmutableHashSet.Create("e1");
        var shardIds = ImmutableHashSet.Create("s1");
        var stats = new ShardRegionStats(ImmutableDictionary<string, int>.Empty.Add("s1", 17), ImmutableHashSet.Create("failed"));
        var state = new ShardCoordinator.CoordinatorState(
            ImmutableDictionary<string, IActorRef>.Empty.Add("s1", region),
            ImmutableDictionary<IActorRef, IImmutableList<string>>.Empty.Add(region, ImmutableList.Create("s1")),
            ImmutableHashSet.Create(proxy), ImmutableHashSet.Create("unallocated"));
        return
        [
            (new ShardingEnvelope("e1", "payload"), "a", "Envelope"),
            (new ShardingEnvelope("e1", null!), "a", "EnvelopeNull"),
            (state, "AA", "CoordinatorState"),
            (ShardCoordinator.CoordinatorState.Empty, "AA", "CoordinatorStateEmpty"),
            (new ShardCoordinator.ShardRegionRegistered(region), "AB", "RegionRegistered"),
            (new ShardCoordinator.ShardRegionProxyRegistered(proxy), "AC", "ProxyRegistered"),
            (new ShardCoordinator.ShardRegionTerminated(region), "AD", "RegionTerminated"),
            (new ShardCoordinator.ShardRegionProxyTerminated(proxy), "AE", "ProxyTerminated"),
            (new ShardCoordinator.ShardHomeAllocated("s1", region), "AF", "HomeAllocated"),
            (new ShardCoordinator.ShardHomeDeallocated("s1"), "AG", "HomeDeallocated"),
            (new ShardCoordinator.Register(region), "BA", "Register"),
            (new ShardCoordinator.RegisterProxy(proxy), "BB", "RegisterProxy"),
            (new ShardCoordinator.RegisterAck(region), "BC", "RegisterAck"),
            (new ShardCoordinator.GetShardHome("s1"), "BD", "GetShardHome"),
            (new ShardCoordinator.ShardHome("s1", region), "BE", "ShardHome"),
            (new ShardCoordinator.HostShard("s1"), "BF", "HostShard"),
            (new ShardCoordinator.ShardStarted("s1"), "BG", "ShardStarted"),
            (new ShardCoordinator.BeginHandOff("s1"), "BH", "BeginHandOff"),
            (new ShardCoordinator.BeginHandOffAck("s1"), "BI", "BeginHandOffAck"),
            (new ShardCoordinator.HandOff("s1"), "BJ", "HandOff"),
            (new ShardCoordinator.ShardStopped("s1"), "BK", "ShardStopped"),
            (new ShardCoordinator.GracefulShutdownRequest(region), "BL", "GracefulShutdown"),
            (new ShardCoordinator.RegionStopped(region), "BM", "RegionStopped"),
            (new EventSourcedRememberEntitiesShardStore.State(entities), "CA", "EntityState"),
            (new EventSourcedRememberEntitiesShardStore.State(), "CA", "EntityStateEmpty"),
            (new EventSourcedRememberEntitiesShardStore.EntitiesStarted(entities), "CE", "EntitiesStarted"),
            (new EventSourcedRememberEntitiesShardStore.EntitiesStarted(), "CE", "EntitiesStartedEmpty"),
            (new EventSourcedRememberEntitiesShardStore.EntitiesStopped(entities), "CF", "EntitiesStopped"),
            (new EventSourcedRememberEntitiesShardStore.EntitiesStopped(ImmutableHashSet<string>.Empty), "CF", "EntitiesStoppedEmpty"),
            (new ShardRegion.StartEntity("e1"), "EA", "StartEntity"),
            (new ShardRegion.StartEntityAck("e1", "s1"), "EB", "StartEntityAck"),
            (Shard.GetShardStats.Instance, "DA", "GetShardStats"),
            (new Shard.ShardStats("s1", 17), "DB", "ShardStats"),
            (GetShardRegionStats.Instance, "DC", "GetRegionStats"),
            (stats, "DD", "RegionStats"),
            (new ShardRegionStats(ImmutableDictionary<string, int>.Empty, ImmutableHashSet<string>.Empty), "DD", "RegionStatsEmpty"),
            (new GetClusterShardingStats(TimeSpan.FromTicks(1234567)), "GS", "GetClusterStats"),
            (new ClusterShardingStats(ImmutableDictionary<Address, ShardRegionStats>.Empty.Add(Owner, stats)), "CS", "ClusterStats"),
            (new ClusterShardingStats(ImmutableDictionary<Address, ShardRegionStats>.Empty), "CS", "ClusterStatsEmpty"),
            (GetCurrentRegions.Instance, "DG", "GetRegions"),
            (new CurrentRegions(ImmutableHashSet.Create(Owner)), "DH", "CurrentRegions"),
            (new CurrentRegions(ImmutableHashSet<Address>.Empty), "DH", "CurrentRegionsEmpty"),
            (Shard.GetCurrentShardState.Instance, "FA", "GetShardState"),
            (new Shard.CurrentShardState("s1", entities), "FB", "CurrentShardState"),
            (GetShardRegionState.Instance, "FC", "GetRegionState"),
            (new ShardState("s1", entities), "FD", "ShardState"),
            (new CurrentShardRegionState(ImmutableHashSet.Create(new ShardState("s1", entities)), ImmutableHashSet.Create("failed")), "FE", "CurrentRegionState"),
            (new CurrentShardRegionState(ImmutableHashSet<ShardState>.Empty, ImmutableHashSet<string>.Empty), "FE", "CurrentRegionStateEmpty"),
            (EventSourcedRememberEntitiesCoordinatorStore.MigrationMarker.Instance, "SM", "MigrationMarker"),
            (new EventSourcedRememberEntitiesCoordinatorStore.State(shardIds, true), "SS", "RememberShardsMarked"),
            (new EventSourcedRememberEntitiesCoordinatorStore.State(ImmutableHashSet<string>.Empty, false), "SS", "RememberShardsEmpty")
        ];
    }
}
