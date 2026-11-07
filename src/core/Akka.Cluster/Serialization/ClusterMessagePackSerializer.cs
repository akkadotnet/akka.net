// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Routing;
using Akka.Serialization;
using Akka.Serialization.V2;
using Akka.Util;

namespace Akka.Cluster.Serialization;

/// <summary>
/// Generated companion to cluster protocol id 5. Both readers remain registered; the global V2
/// toggle selects id 45 writes. Gossip stays uncompressed and its process-local deadline is omitted.
/// </summary>
internal sealed class ClusterMessagePackSerializer : SerializerV2
{
    internal const int SerializerId = 45;
    private readonly ClusterMessagePackCodec _codec;
    private readonly bool _legacyHeartbeat;

    public ClusterMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new ClusterMessagePackCodec(system);
        // Reading this setting must not start the Cluster extension just to construct a serializer.
        _legacyHeartbeat = system.Settings.Config.GetBoolean("akka.cluster.use-legacy-heartbeat-message", false);
    }

    public override int Identifier => SerializerId;

    public override string Manifest(object obj) => obj switch
    {
        ClusterHeartbeatSender.Heartbeat => _legacyHeartbeat ? ClusterMessageSerializer.HeartBeatManifestPre1419 : ClusterMessageSerializer.HeartBeatManifest,
        ClusterHeartbeatSender.HeartbeatRsp => _legacyHeartbeat ? ClusterMessageSerializer.HeartBeatRspManifestPre1419 : ClusterMessageSerializer.HeartBeatRspManifest,
        GossipEnvelope => ClusterMessageSerializer.GossipEnvelopeManifest,
        GossipStatus => ClusterMessageSerializer.GossipStatusManifest,
        InternalClusterAction.Join => ClusterMessageSerializer.JoinManifest,
        InternalClusterAction.Welcome => ClusterMessageSerializer.WelcomeManifest,
        ClusterUserAction.Leave => ClusterMessageSerializer.LeaveManifest,
        ClusterUserAction.Down => ClusterMessageSerializer.DownManifest,
        InternalClusterAction.InitJoin => ClusterMessageSerializer.InitJoinManifest,
        InternalClusterAction.InitJoinAck => ClusterMessageSerializer.InitJoinAckManifest,
        InternalClusterAction.InitJoinNack => ClusterMessageSerializer.InitJoinNackManifest,
        InternalClusterAction.ExitingConfirmed => ClusterMessageSerializer.ExitingConfirmedManifest,
        ClusterRouterPool => ClusterMessageSerializer.ClusterRouterPoolManifest,
        _ => throw new ArgumentException($"Unsupported cluster message [{obj.GetType()}]", nameof(obj))
    };

    public override int Serialize(object obj, IBufferWriter<byte> writer) => _codec.Serialize(ToWire(obj), writer);
    public override int SizeHint(object obj) => _codec.SizeHint(ToWire(obj));

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => _codec.Deserialize(bytes, manifest) switch
    {
        ClusterHeartbeatWire heartbeat => new ClusterHeartbeatSender.Heartbeat(FromWire(heartbeat.From), heartbeat.Sequence, heartbeat.Time),
        ClusterHeartbeatRspWire heartbeat => new ClusterHeartbeatSender.HeartbeatRsp(FromWire(heartbeat.From), heartbeat.Sequence, heartbeat.Time),
        ClusterOldHeartbeatWire heartbeat => new ClusterHeartbeatSender.Heartbeat(FromWire(heartbeat.From), -1, -1),
        ClusterOldHeartbeatRspWire heartbeat => new ClusterHeartbeatSender.HeartbeatRsp(FromWire(heartbeat.From), -1, -1),
        ClusterEnvelopeWire envelope => new GossipEnvelope(FromWire(envelope.From), FromWire(envelope.To), FromWire(envelope.Gossip)),
        ClusterStatusWire status => new GossipStatus(FromWire(status.From), FromWire(status.Version)),
        ClusterJoinWire join => new InternalClusterAction.Join(FromWire(join.Node), join.Roles.ToImmutableHashSet(), Version(join.AppVersion)),
        ClusterWelcomeWire welcome => new InternalClusterAction.Welcome(FromWire(welcome.From), FromWire(welcome.Gossip)),
        ClusterLeaveWire leave => new ClusterUserAction.Leave(FromWire(leave.Address)),
        ClusterDownWire down => new ClusterUserAction.Down(FromWire(down.Address)),
        ClusterInitJoinWire => new InternalClusterAction.InitJoin(),
        ClusterInitJoinAckWire ack => new InternalClusterAction.InitJoinAck(FromWire(ack.Address)),
        ClusterInitJoinNackWire nack => new InternalClusterAction.InitJoinNack(FromWire(nack.Address)),
        ClusterExitingWire exiting => new InternalClusterAction.ExitingConfirmed(FromWire(exiting.Address)),
        ClusterPoolWire pool => new ClusterRouterPool((Pool)pool.Pool,
            new ClusterRouterPoolSettings(pool.Total, pool.PerNode, pool.AllowLocal, string.IsNullOrEmpty(pool.Role) ? null! : pool.Role)),
        var wire => throw new SerializationException($"Unknown cluster wire message [{wire.GetType()}]")
    };

    private IClusterWireMessage ToWire(object obj) => obj switch
    {
        ClusterHeartbeatSender.Heartbeat heartbeat when _legacyHeartbeat => new ClusterOldHeartbeatWire(heartbeat.From),
        ClusterHeartbeatSender.Heartbeat heartbeat => new ClusterHeartbeatWire(heartbeat.From, heartbeat.SequenceNr, heartbeat.CreationTimeNanos),
        ClusterHeartbeatSender.HeartbeatRsp heartbeat when _legacyHeartbeat => new ClusterOldHeartbeatRspWire(ToWire(heartbeat.From)),
        ClusterHeartbeatSender.HeartbeatRsp heartbeat => new ClusterHeartbeatRspWire(ToWire(heartbeat.From), heartbeat.SequenceNr, heartbeat.CreationTimeNanos),
        GossipEnvelope envelope => new ClusterEnvelopeWire(ToWire(envelope.From), ToWire(envelope.To), ToWire(envelope.Gossip)),
        GossipStatus status => new ClusterStatusWire(ToWire(status.From), ToWire(status.Version)),
        InternalClusterAction.Join join => new ClusterJoinWire(ToWire(join.Node), join.Roles.OrderBy(r => r, StringComparer.Ordinal).ToArray(), join.AppVersion.Version),
        InternalClusterAction.Welcome welcome => new ClusterWelcomeWire(ToWire(welcome.From), ToWire(welcome.Gossip)),
        ClusterUserAction.Leave leave => new ClusterLeaveWire(leave.Address),
        ClusterUserAction.Down down => new ClusterDownWire(down.Address),
        InternalClusterAction.InitJoin => new ClusterInitJoinWire(),
        InternalClusterAction.InitJoinAck ack => new ClusterInitJoinAckWire(ack.Address),
        InternalClusterAction.InitJoinNack nack => new ClusterInitJoinNackWire(nack.Address),
        InternalClusterAction.ExitingConfirmed exiting => new ClusterExitingWire(ToWire(exiting.Address)),
        ClusterRouterPool pool => new ClusterPoolWire(pool.Local, pool.Settings.TotalInstances,
            pool.Settings.MaxInstancesPerNode, pool.Settings.AllowLocalRoutees, pool.Settings.UseRole),
        _ => throw new ArgumentException($"Unsupported cluster message [{obj.GetType()}]", nameof(obj))
    };

    private static ClusterAddressWire ToWire(UniqueAddress address) => new(address.Address, address.Uid);
    private static UniqueAddress FromWire(ClusterAddressWire address) => new(FromWire(address.Address), address.Uid);
    // The old AddressData reader treats port zero as an absent port.
    private static Address FromWire(Address address) => address.Port == 0
        ? new Address(address.Protocol, address.System, address.Host, null) : address;
    private static AppVersion Version(string? version) => string.IsNullOrEmpty(version) ? AppVersion.Zero : AppVersion.Create(version);
    private static ClusterClockWire[] ToWire(VectorClock clock) => clock.Versions
        .Select(pair => new ClusterClockWire(pair.Key.ToString(), pair.Value)).ToArray();
    private static VectorClock FromWire(ClusterClockWire[] clock) => VectorClock.Create(clock
        .ToImmutableSortedDictionary(entry => VectorClock.Node.FromHash(entry.Hash), entry => entry.Counter));

    private static ClusterGossipWire ToWire(Gossip gossip)
    {
        // Addresses occur throughout reachability. Keep one canonical table instead of repeating
        // full addresses per record; the latter can multiply a large cluster's gossip frame size.
        var addresses = gossip.Members.Select(member => member.UniqueAddress).Concat(gossip.Tombstones.Keys)
            .Distinct().OrderBy(address => address).ToArray();
        var addressIndexes = addresses.Select((address, index) => (address, index)).ToDictionary(pair => pair.address, pair => pair.index);
        var roles = gossip.Members.SelectMany(member => member.Roles).Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal).ToArray();
        var roleIndexes = roles.Select((role, index) => (role, index)).ToDictionary(pair => pair.role, pair => pair.index);
        var versions = gossip.Members.Select(member => member.AppVersion.Version).Distinct(StringComparer.Ordinal)
            .OrderBy(version => version, StringComparer.Ordinal).ToArray();
        var versionIndexes = versions.Select((version, index) => (version, index)).ToDictionary(pair => pair.version, pair => pair.index);
        int Index(UniqueAddress address) => addressIndexes.TryGetValue(address, out var index) ? index
            : throw new ArgumentException($"Unknown address [{address}] in cluster gossip");

        return new ClusterGossipWire(addresses.Select(ToWire).ToArray(), roles, versions,
            gossip.Members.Select(member => new ClusterMemberWire(Index(member.UniqueAddress), member.UpNumber,
                (int)member.Status, member.Roles.OrderBy(role => role, StringComparer.Ordinal).Select(role => roleIndexes[role]).ToArray(),
                versionIndexes[member.AppVersion.Version])).ToArray(),
            gossip.Overview.Seen.OrderBy(address => address).Select(Index).ToArray(),
            gossip.Overview.Reachability.Records.OrderBy(record => record.Observer).ThenBy(record => record.Subject)
                .Select(record => new ClusterReachabilityWire(Index(record.Observer), Index(record.Subject), (int)record.Status, record.Version)).ToArray(),
            gossip.Overview.Reachability.Versions.OrderBy(pair => pair.Key)
                .Select(pair => new ClusterObserverWire(Index(pair.Key), pair.Value)).ToArray(),
            ToWire(gossip.Version), gossip.Tombstones.OrderBy(pair => pair.Key)
                .Select(pair => new ClusterTombstoneWire(Index(pair.Key), pair.Value)).ToArray());
    }

    private static Gossip FromWire(ClusterGossipWire gossip)
    {
        var addresses = gossip.Addresses.Select(FromWire).ToArray();
        return new Gossip(gossip.Members.Select(member => Member.Create(addresses[member.Address], member.UpNumber,
                (MemberStatus)member.Status, member.Roles.Select(index => gossip.Roles[index]).ToImmutableHashSet(),
                gossip.AppVersions.Length == 0 ? AppVersion.Zero : Version(gossip.AppVersions[member.AppVersion])))
                .ToImmutableSortedSet(Member.Ordering),
            new GossipOverview(gossip.Seen.Select(index => addresses[index]).ToImmutableHashSet(),
                new Reachability(gossip.Reachability.Select(record => new Reachability.Record(addresses[record.Observer], addresses[record.Subject],
                        (Reachability.ReachabilityStatus)record.Status, record.Version)).ToImmutableList(),
                    gossip.Observers.ToImmutableDictionary(observer => addresses[observer.Address], observer => observer.Version))),
            FromWire(gossip.Clock), gossip.Tombstones.ToImmutableDictionary(tombstone => addresses[tombstone.Address], tombstone => tombstone.Timestamp));
    }
}

internal interface IClusterWireMessage;

[AkkaSerializer<IClusterWireMessage>("akka-cluster-v2-codec", ClusterMessagePackSerializer.SerializerId)]
[AkkaSerializerFormatter<Address, AddressFormatter>]
internal sealed partial class ClusterMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

[AkkaSerializable(Manifest = ClusterMessageSerializer.HeartBeatManifest)]
internal sealed record ClusterHeartbeatWire(
    [property: AkkaField(1)] Address From,
    [property: AkkaField(2)] long Sequence,
    [property: AkkaField(3)] long Time) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.HeartBeatRspManifest)]
internal sealed record ClusterHeartbeatRspWire(
    [property: AkkaField(1)] ClusterAddressWire From,
    [property: AkkaField(2)] long Sequence,
    [property: AkkaField(3)] long Time) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.HeartBeatManifestPre1419)]
internal sealed record ClusterOldHeartbeatWire(
    [property: AkkaField(1)] Address From) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.HeartBeatRspManifestPre1419)]
internal sealed record ClusterOldHeartbeatRspWire(
    [property: AkkaField(1)] ClusterAddressWire From) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.GossipEnvelopeManifest)]
internal sealed record ClusterEnvelopeWire(
    [property: AkkaField(1)] ClusterAddressWire From,
    [property: AkkaField(2)] ClusterAddressWire To,
    [property: AkkaField(3)] ClusterGossipWire Gossip) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.GossipStatusManifest)]
internal sealed record ClusterStatusWire(
    [property: AkkaField(1)] ClusterAddressWire From,
    [property: AkkaField(2)] ClusterClockWire[] Version) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.JoinManifest)]
internal sealed record ClusterJoinWire(
    [property: AkkaField(1)] ClusterAddressWire Node,
    [property: AkkaField(2)] string[] Roles,
    [property: AkkaField(3)] string? AppVersion) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.WelcomeManifest)]
internal sealed record ClusterWelcomeWire(
    [property: AkkaField(1)] ClusterAddressWire From,
    [property: AkkaField(2)] ClusterGossipWire Gossip) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.LeaveManifest)]
internal sealed record ClusterLeaveWire(
    [property: AkkaField(1)] Address Address) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.DownManifest)]
internal sealed record ClusterDownWire(
    [property: AkkaField(1)] Address Address) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.InitJoinManifest, AllowEmpty = true)]
internal sealed record ClusterInitJoinWire : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.InitJoinAckManifest)]
internal sealed record ClusterInitJoinAckWire(
    [property: AkkaField(1)] Address Address) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.InitJoinNackManifest)]
internal sealed record ClusterInitJoinNackWire(
    [property: AkkaField(1)] Address Address) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.ExitingConfirmedManifest)]
internal sealed record ClusterExitingWire(
    [property: AkkaField(1)] ClusterAddressWire Address) : IClusterWireMessage;

[AkkaSerializable(Manifest = ClusterMessageSerializer.ClusterRouterPoolManifest)]
internal sealed record ClusterPoolWire(
    [property: AkkaField(1)] object Pool,
    [property: AkkaField(2)] int Total,
    [property: AkkaField(3)] int PerNode,
    [property: AkkaField(4)] bool AllowLocal,
    [property: AkkaField(5)] string? Role) : IClusterWireMessage;

[AkkaSerializable]
internal sealed record ClusterAddressWire(
    [property: AkkaField(1)] Address Address,
    [property: AkkaField(2)] long Uid);

[AkkaSerializable]
internal sealed record ClusterClockWire(
    [property: AkkaField(1)] string Hash,
    [property: AkkaField(2)] long Counter);

[AkkaSerializable]
internal sealed record ClusterMemberWire(
    [property: AkkaField(1)] int Address,
    [property: AkkaField(2)] int UpNumber,
    [property: AkkaField(3)] int Status,
    [property: AkkaField(4)] int[] Roles,
    [property: AkkaField(5)] int AppVersion);

[AkkaSerializable]
internal sealed record ClusterReachabilityWire(
    [property: AkkaField(1)] int Observer,
    [property: AkkaField(2)] int Subject,
    [property: AkkaField(3)] int Status,
    [property: AkkaField(4)] long Version);

[AkkaSerializable]
internal sealed record ClusterObserverWire(
    [property: AkkaField(1)] int Address,
    [property: AkkaField(2)] long Version);

[AkkaSerializable]
internal sealed record ClusterTombstoneWire(
    [property: AkkaField(1)] int Address,
    [property: AkkaField(2)] long Timestamp);

[AkkaSerializable]
internal sealed record ClusterGossipWire(
    [property: AkkaField(1)] ClusterAddressWire[] Addresses,
    [property: AkkaField(2)] string[] Roles,
    [property: AkkaField(3)] string[] AppVersions,
    [property: AkkaField(4)] ClusterMemberWire[] Members,
    [property: AkkaField(5)] int[] Seen,
    [property: AkkaField(6)] ClusterReachabilityWire[] Reachability,
    [property: AkkaField(7)] ClusterObserverWire[] Observers,
    [property: AkkaField(8)] ClusterClockWire[] Clock,
    [property: AkkaField(9)] ClusterTombstoneWire[] Tombstones);

