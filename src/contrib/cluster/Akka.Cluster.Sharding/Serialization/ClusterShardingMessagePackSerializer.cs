// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
using Akka.Serialization;
using Akka.Serialization.V2;

namespace Akka.Cluster.Sharding.Serialization;

/// <summary>
/// Generated counterpart of sharding id 13. Id 53 is a reader until the global V2 toggle selects
/// it for writes. Actor references and user payload envelopes use the owning actor system.
/// </summary>
internal sealed class ClusterShardingMessagePackSerializer : SerializerV2
{
    internal const int SerializerId = 53;
    private readonly ClusterShardingMessagePackCodec _codec;
    private static readonly ShardingGetShardStatsWire GetShardStatsMessage = new();
    private static readonly ShardingGetRegionStatsWire GetRegionStatsMessage = new();
    private static readonly ShardingGetRegionsWire GetRegionsMessage = new();
    private static readonly ShardingGetShardStateWire GetShardStateMessage = new();
    private static readonly ShardingGetRegionStateWire GetRegionStateMessage = new();
    private static readonly ShardingMigrationMarkerWire MigrationMarkerMessage = new();

    public ClusterShardingMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new ClusterShardingMessagePackCodec(system);
    }

    public override int Identifier => SerializerId;

    public override string Manifest(object obj)
    {
        var manifest = ClusterShardingMessageSerializer.GetManifest(obj);
        return manifest.Length > 0 ? manifest
            : throw new ArgumentException($"Unsupported sharding message [{obj.GetType()}]", nameof(obj));
    }

    public override int Serialize(object obj, IBufferWriter<byte> writer) => _codec.Serialize(ToWire(obj), writer);
    public override int SizeHint(object obj) => _codec.SizeHint(ToWire(obj));

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => _codec.Deserialize(bytes, manifest) switch
    {
        ShardingEnvelopeWire data => new ShardingEnvelope(data.EntityId, data.Message!),
        ShardingCoordinatorWire data => FromWire(data),
        ShardingRegionRegisteredWire data => new ShardCoordinator.ShardRegionRegistered(Reference(data.Ref)),
        ShardingProxyRegisteredWire data => new ShardCoordinator.ShardRegionProxyRegistered(Reference(data.Ref)),
        ShardingRegionTerminatedWire data => new ShardCoordinator.ShardRegionTerminated(Reference(data.Ref)),
        ShardingProxyTerminatedWire data => new ShardCoordinator.ShardRegionProxyTerminated(Reference(data.Ref)),
        ShardingRegisterWire data => new ShardCoordinator.Register(Reference(data.Ref)),
        ShardingRegisterProxyWire data => new ShardCoordinator.RegisterProxy(Reference(data.Ref)),
        ShardingRegisterAckWire data => new ShardCoordinator.RegisterAck(Reference(data.Ref)),
        ShardingGracefulShutdownWire data => new ShardCoordinator.GracefulShutdownRequest(Reference(data.Ref)),
        ShardingRegionStoppedWire data => new ShardCoordinator.RegionStopped(Reference(data.Ref)),
        ShardingHomeAllocatedWire data => new ShardCoordinator.ShardHomeAllocated(data.Shard, Reference(data.Ref)),
        ShardingShardHomeWire data => new ShardCoordinator.ShardHome(data.Shard, Reference(data.Ref)),
        ShardingHomeDeallocatedWire data => new ShardCoordinator.ShardHomeDeallocated(data.Shard),
        ShardingGetShardHomeWire data => new ShardCoordinator.GetShardHome(data.Shard),
        ShardingHostShardWire data => new ShardCoordinator.HostShard(data.Shard),
        ShardingShardStartedWire data => new ShardCoordinator.ShardStarted(data.Shard),
        ShardingBeginHandOffWire data => new ShardCoordinator.BeginHandOff(data.Shard),
        ShardingBeginHandOffAckWire data => new ShardCoordinator.BeginHandOffAck(data.Shard),
        ShardingHandOffWire data => new ShardCoordinator.HandOff(data.Shard),
        ShardingShardStoppedWire data => new ShardCoordinator.ShardStopped(data.Shard),
        ShardingEntityStateWire data => new EventSourcedRememberEntitiesShardStore.State(data.Entities.ToImmutableHashSet()),
        ShardingEntitiesStartedWire data => new EventSourcedRememberEntitiesShardStore.EntitiesStarted(data.Entities.ToImmutableHashSet()),
        ShardingEntitiesStoppedWire data => new EventSourcedRememberEntitiesShardStore.EntitiesStopped(data.Entities.ToImmutableHashSet()),
        ShardingStartEntityWire data => new ShardRegion.StartEntity(data.Entity),
        ShardingStartEntityAckWire data => new ShardRegion.StartEntityAck(data.Entity, data.Shard),
        ShardingShardStatsWire data => new Shard.ShardStats(data.Shard, data.Count),
        ShardingRegionStatsWire data => FromWire(data),
        ShardingGetClusterStatsWire data => new GetClusterShardingStats(TimeSpan.FromTicks(data.Ticks)),
        ShardingClusterStatsWire data => new ClusterShardingStats(data.Regions.ToImmutableDictionary(entry => Normalize(entry.Address), entry => FromWire(entry.Stats))),
        ShardingCurrentRegionsWire data => new CurrentRegions(data.Regions.Select(Normalize).ToImmutableHashSet()),
        ShardingCurrentShardStateWire data => new Shard.CurrentShardState(data.Shard, data.Entities.ToImmutableHashSet()),
        ShardingShardStateWire data => FromWire(data),
        ShardingCurrentRegionStateWire data => new CurrentShardRegionState(data.Shards.Select(shard => FromWire(shard)).ToImmutableHashSet(), data.Failed.ToImmutableHashSet()),
        ShardingRememberShardsWire data => new EventSourcedRememberEntitiesCoordinatorStore.State(data.Shards.ToImmutableHashSet(), data.Marker),
        ShardingGetShardStatsWire => Shard.GetShardStats.Instance,
        ShardingGetRegionStatsWire => GetShardRegionStats.Instance,
        ShardingGetRegionsWire => GetCurrentRegions.Instance,
        ShardingGetShardStateWire => Shard.GetCurrentShardState.Instance,
        ShardingGetRegionStateWire => GetShardRegionState.Instance,
        ShardingMigrationMarkerWire => EventSourcedRememberEntitiesCoordinatorStore.MigrationMarker.Instance,
        ShardingOldEntityStartedWire data => new EventSourcedRememberEntitiesShardStore.EntitiesStarted(ImmutableHashSet.Create(data.Entity)),
        ShardingOldEntityStoppedWire data => new EventSourcedRememberEntitiesShardStore.EntitiesStopped(ImmutableHashSet.Create(data.Entity)),
        var wire => throw new SerializationException($"Unknown sharding wire message [{wire.GetType()}]")
    };

    private static IShardingWireMessage ToWire(object obj) => obj switch
    {
        ShardingEnvelope message => new ShardingEnvelopeWire(message.EntityId, message.Message),
        ShardCoordinator.CoordinatorState message => ToWire(message),
        ShardCoordinator.ShardRegionRegistered message => new ShardingRegionRegisteredWire(message.Region),
        ShardCoordinator.ShardRegionProxyRegistered message => new ShardingProxyRegisteredWire(message.RegionProxy),
        ShardCoordinator.ShardRegionTerminated message => new ShardingRegionTerminatedWire(message.Region),
        ShardCoordinator.ShardRegionProxyTerminated message => new ShardingProxyTerminatedWire(message.RegionProxy),
        ShardCoordinator.Register message => new ShardingRegisterWire(message.ShardRegion),
        ShardCoordinator.RegisterProxy message => new ShardingRegisterProxyWire(message.ShardRegionProxy),
        ShardCoordinator.RegisterAck message => new ShardingRegisterAckWire(message.Coordinator),
        ShardCoordinator.GracefulShutdownRequest message => new ShardingGracefulShutdownWire(message.ShardRegion),
        ShardCoordinator.RegionStopped message => new ShardingRegionStoppedWire(message.ShardRegion),
        ShardCoordinator.ShardHomeAllocated message => new ShardingHomeAllocatedWire(message.Shard, message.Region),
        ShardCoordinator.ShardHome message => new ShardingShardHomeWire(message.Shard, message.Ref),
        ShardCoordinator.ShardHomeDeallocated message => new ShardingHomeDeallocatedWire(message.Shard),
        ShardCoordinator.GetShardHome message => new ShardingGetShardHomeWire(message.Shard),
        ShardCoordinator.HostShard message => new ShardingHostShardWire(message.Shard),
        ShardCoordinator.ShardStarted message => new ShardingShardStartedWire(message.Shard),
        ShardCoordinator.BeginHandOff message => new ShardingBeginHandOffWire(message.Shard),
        ShardCoordinator.BeginHandOffAck message => new ShardingBeginHandOffAckWire(message.Shard),
        ShardCoordinator.HandOff message => new ShardingHandOffWire(message.Shard),
        ShardCoordinator.ShardStopped message => new ShardingShardStoppedWire(message.Shard),
        EventSourcedRememberEntitiesShardStore.State message => new ShardingEntityStateWire(Sorted(message.Entities)),
        EventSourcedRememberEntitiesShardStore.EntitiesStarted message => new ShardingEntitiesStartedWire(Sorted(message.Entities)),
        EventSourcedRememberEntitiesShardStore.EntitiesStopped message => new ShardingEntitiesStoppedWire(Sorted(message.Entities)),
        ShardRegion.StartEntity message => new ShardingStartEntityWire(message.EntityId),
        ShardRegion.StartEntityAck message => new ShardingStartEntityAckWire(message.EntityId, message.ShardId),
        Shard.ShardStats message => new ShardingShardStatsWire(message.ShardId, message.EntityCount),
        ShardRegionStats message => ToWire(message),
        GetClusterShardingStats message => new ShardingGetClusterStatsWire(message.Timeout.Ticks),
        ClusterShardingStats message => new ShardingClusterStatsWire(message.Regions.OrderBy(pair => pair.Key, Address.Comparer).Select(pair => new ShardingRegionEntryWire(pair.Key, ToWire(pair.Value))).ToArray()),
        CurrentRegions message => new ShardingCurrentRegionsWire(message.Regions.OrderBy(address => address, Address.Comparer).ToArray()),
        Shard.CurrentShardState message => new ShardingCurrentShardStateWire(message.ShardId, Sorted(message.EntityIds)),
        ShardState message => ToWire(message),
        CurrentShardRegionState message => new ShardingCurrentRegionStateWire(message.Shards.OrderBy(shard => shard.ShardId, StringComparer.Ordinal).Select(shard => ToWire(shard)).ToArray(), Sorted(message.Failed)),
        EventSourcedRememberEntitiesCoordinatorStore.State message => new ShardingRememberShardsWire(Sorted(message.Shards), message.WrittenMigrationMarker),
        Shard.GetShardStats => GetShardStatsMessage,
        GetShardRegionStats => GetRegionStatsMessage,
        GetCurrentRegions => GetRegionsMessage,
        Shard.GetCurrentShardState => GetShardStateMessage,
        GetShardRegionState => GetRegionStateMessage,
        EventSourcedRememberEntitiesCoordinatorStore.MigrationMarker => MigrationMarkerMessage,
        _ => throw new ArgumentException($"Unsupported sharding message [{obj.GetType()}]", nameof(obj))
    };

    // Protobuf resolves an empty NoSender path through the actual receiver provider. Local
    // providers yield DeadLetters; remote providers yield NoSender. Retain both legacy results.
    private IActorRef Reference(IActorRef? value) => value ?? system.Provider.ResolveActorRef(string.Empty);

    private static string[] Sorted(IEnumerable<string> values) => values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    private static IActorRef[] Sorted(IEnumerable<IActorRef> values) => values
        .OrderBy(Akka.Serialization.Serialization.SerializedActorPath, StringComparer.Ordinal).ToArray();
    private static Address Normalize(Address address) => address.Port == 0
        ? new Address(address.Protocol, address.System, address.Host, null) : address;

    private static ShardingCoordinatorWire ToWire(ShardCoordinator.CoordinatorState state) => new(
        state.Shards.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new ShardingAllocationWire(pair.Key, pair.Value)).ToArray(),
        Sorted(state.Regions.Keys), Sorted(state.RegionProxies), Sorted(state.UnallocatedShards));

    private ShardCoordinator.CoordinatorState FromWire(ShardingCoordinatorWire state)
    {
        var shards = state.Shards.ToImmutableDictionary(entry => entry.Shard, entry => Reference(entry.Region));
        var regions = state.Regions.Select(Reference).ToImmutableDictionary(region => region, _ => (IImmutableList<string>)ImmutableList<string>.Empty);
        foreach (var entry in state.Shards)
        {
            var region = Reference(entry.Region);
            regions = regions.SetItem(region, regions[region].Add(entry.Shard));
        }
        // The legacy snapshot stores region identities, reconstructs their shard lists, and does
        // not persist RememberEntities. The coordinator reapplies that runtime setting on recovery.
        return new ShardCoordinator.CoordinatorState(shards, regions, state.Proxies.Select(Reference).ToImmutableHashSet(), state.Unallocated.ToImmutableHashSet());
    }

    private static ShardingRegionStatsWire ToWire(ShardRegionStats stats) => new(
        stats.Stats.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new ShardingCountWire(pair.Key, pair.Value)).ToArray(),
        Sorted(stats.Failed));
    private static ShardRegionStats FromWire(ShardingRegionStatsWire stats) => new(
        stats.Stats.ToImmutableDictionary(entry => entry.Shard, entry => entry.Count), stats.Failed.ToImmutableHashSet());
    private static ShardingShardStateWire ToWire(ShardState state) => new(state.ShardId, Sorted(state.EntityIds));
    private static ShardState FromWire(ShardingShardStateWire state) => new(state.Shard, state.Entities.ToImmutableHashSet());
}

internal interface IShardingWireMessage;

[AkkaSerializer<IShardingWireMessage>("akka-sharding-v2-codec", ClusterShardingMessagePackSerializer.SerializerId)]
[AkkaSerializerFormatter<Address, AddressFormatter>]
internal sealed partial class ClusterShardingMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

[AkkaSerializable(Manifest = "a")]
internal sealed record ShardingEnvelopeWire(
    [property: AkkaField(1)] string EntityId,
    [property: AkkaField(2)] object? Message) : IShardingWireMessage;

[AkkaSerializable(Manifest = "AA")]
internal sealed record ShardingCoordinatorWire(
    [property: AkkaField(1)] ShardingAllocationWire[] Shards,
    [property: AkkaField(2)] IActorRef?[] Regions,
    [property: AkkaField(3)] IActorRef?[] Proxies,
    [property: AkkaField(4)] string[] Unallocated) : IShardingWireMessage;

[AkkaSerializable(Manifest = "AB")]
internal sealed record ShardingRegionRegisteredWire(
    [property: AkkaField(1)] IActorRef? Ref) : IShardingWireMessage;

[AkkaSerializable(Manifest = "AC")]
internal sealed record ShardingProxyRegisteredWire(
    [property: AkkaField(1)] IActorRef? Ref) : IShardingWireMessage;

[AkkaSerializable(Manifest = "AD")]
internal sealed record ShardingRegionTerminatedWire(
    [property: AkkaField(1)] IActorRef? Ref) : IShardingWireMessage;

[AkkaSerializable(Manifest = "AE")]
internal sealed record ShardingProxyTerminatedWire(
    [property: AkkaField(1)] IActorRef? Ref) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BA")]
internal sealed record ShardingRegisterWire(
    [property: AkkaField(1)] IActorRef? Ref) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BB")]
internal sealed record ShardingRegisterProxyWire(
    [property: AkkaField(1)] IActorRef? Ref) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BC")]
internal sealed record ShardingRegisterAckWire(
    [property: AkkaField(1)] IActorRef? Ref) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BL")]
internal sealed record ShardingGracefulShutdownWire(
    [property: AkkaField(1)] IActorRef? Ref) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BM")]
internal sealed record ShardingRegionStoppedWire(
    [property: AkkaField(1)] IActorRef? Ref) : IShardingWireMessage;

[AkkaSerializable(Manifest = "AF")]
internal sealed record ShardingHomeAllocatedWire(
    [property: AkkaField(1)] string Shard,
    [property: AkkaField(2)] IActorRef? Ref) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BE")]
internal sealed record ShardingShardHomeWire(
    [property: AkkaField(1)] string Shard,
    [property: AkkaField(2)] IActorRef? Ref) : IShardingWireMessage;

[AkkaSerializable(Manifest = "AG")]
internal sealed record ShardingHomeDeallocatedWire(
    [property: AkkaField(1)] string Shard) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BD")]
internal sealed record ShardingGetShardHomeWire(
    [property: AkkaField(1)] string Shard) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BF")]
internal sealed record ShardingHostShardWire(
    [property: AkkaField(1)] string Shard) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BG")]
internal sealed record ShardingShardStartedWire(
    [property: AkkaField(1)] string Shard) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BH")]
internal sealed record ShardingBeginHandOffWire(
    [property: AkkaField(1)] string Shard) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BI")]
internal sealed record ShardingBeginHandOffAckWire(
    [property: AkkaField(1)] string Shard) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BJ")]
internal sealed record ShardingHandOffWire(
    [property: AkkaField(1)] string Shard) : IShardingWireMessage;

[AkkaSerializable(Manifest = "BK")]
internal sealed record ShardingShardStoppedWire(
    [property: AkkaField(1)] string Shard) : IShardingWireMessage;

[AkkaSerializable(Manifest = "CA")]
internal sealed record ShardingEntityStateWire(
    [property: AkkaField(1)] string[] Entities) : IShardingWireMessage;

[AkkaSerializable(Manifest = "CE")]
internal sealed record ShardingEntitiesStartedWire(
    [property: AkkaField(1)] string[] Entities) : IShardingWireMessage;

[AkkaSerializable(Manifest = "CF")]
internal sealed record ShardingEntitiesStoppedWire(
    [property: AkkaField(1)] string[] Entities) : IShardingWireMessage;

[AkkaSerializable(Manifest = "EA")]
internal sealed record ShardingStartEntityWire(
    [property: AkkaField(1)] string Entity) : IShardingWireMessage;

[AkkaSerializable(Manifest = "EB")]
internal sealed record ShardingStartEntityAckWire(
    [property: AkkaField(1)] string Entity,
    [property: AkkaField(2)] string Shard) : IShardingWireMessage;

[AkkaSerializable(Manifest = "DB")]
internal sealed record ShardingShardStatsWire(
    [property: AkkaField(1)] string Shard,
    [property: AkkaField(2)] int Count) : IShardingWireMessage;

[AkkaSerializable(Manifest = "DD")]
internal sealed record ShardingRegionStatsWire(
    [property: AkkaField(1)] ShardingCountWire[] Stats,
    [property: AkkaField(2)] string[] Failed) : IShardingWireMessage;

[AkkaSerializable(Manifest = "GS")]
internal sealed record ShardingGetClusterStatsWire(
    [property: AkkaField(1)] long Ticks) : IShardingWireMessage;

[AkkaSerializable(Manifest = "CS")]
internal sealed record ShardingClusterStatsWire(
    [property: AkkaField(1)] ShardingRegionEntryWire[] Regions) : IShardingWireMessage;

[AkkaSerializable(Manifest = "DH")]
internal sealed record ShardingCurrentRegionsWire(
    [property: AkkaField(1)] Address[] Regions) : IShardingWireMessage;

[AkkaSerializable(Manifest = "FB")]
internal sealed record ShardingCurrentShardStateWire(
    [property: AkkaField(1)] string Shard,
    [property: AkkaField(2)] string[] Entities) : IShardingWireMessage;

[AkkaSerializable(Manifest = "FD")]
internal sealed record ShardingShardStateWire(
    [property: AkkaField(1)] string Shard,
    [property: AkkaField(2)] string[] Entities) : IShardingWireMessage;

[AkkaSerializable(Manifest = "FE")]
internal sealed record ShardingCurrentRegionStateWire(
    [property: AkkaField(1)] ShardingShardStateWire[] Shards,
    [property: AkkaField(2)] string[] Failed) : IShardingWireMessage;

[AkkaSerializable(Manifest = "SS")]
internal sealed record ShardingRememberShardsWire(
    [property: AkkaField(1)] string[] Shards,
    [property: AkkaField(2)] bool Marker) : IShardingWireMessage;

[AkkaSerializable(Manifest = "DA", AllowEmpty = true)]
internal sealed record ShardingGetShardStatsWire : IShardingWireMessage;

[AkkaSerializable(Manifest = "DC", AllowEmpty = true)]
internal sealed record ShardingGetRegionStatsWire : IShardingWireMessage;

[AkkaSerializable(Manifest = "DG", AllowEmpty = true)]
internal sealed record ShardingGetRegionsWire : IShardingWireMessage;

[AkkaSerializable(Manifest = "FA", AllowEmpty = true)]
internal sealed record ShardingGetShardStateWire : IShardingWireMessage;

[AkkaSerializable(Manifest = "FC", AllowEmpty = true)]
internal sealed record ShardingGetRegionStateWire : IShardingWireMessage;

[AkkaSerializable(Manifest = "SM", AllowEmpty = true)]
internal sealed record ShardingMigrationMarkerWire : IShardingWireMessage;

[AkkaSerializable(Manifest = "CB")]
internal sealed record ShardingOldEntityStartedWire(
    [property: AkkaField(1)] string Entity) : IShardingWireMessage;

[AkkaSerializable(Manifest = "CD")]
internal sealed record ShardingOldEntityStoppedWire(
    [property: AkkaField(1)] string Entity) : IShardingWireMessage;

[AkkaSerializable]
internal sealed record ShardingAllocationWire(
    [property: AkkaField(1)] string Shard,
    [property: AkkaField(2)] IActorRef? Region);

[AkkaSerializable]
internal sealed record ShardingCountWire(
    [property: AkkaField(1)] string Shard,
    [property: AkkaField(2)] int Count);

[AkkaSerializable]
internal sealed record ShardingRegionEntryWire(
    [property: AkkaField(1)] Address Address,
    [property: AkkaField(2)] ShardingRegionStatsWire Stats);

