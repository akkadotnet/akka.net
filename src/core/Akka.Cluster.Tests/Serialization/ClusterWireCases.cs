// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Cluster.Serialization;
using Akka.Routing;
using Akka.Util;

namespace Akka.Cluster.Tests.Serialization;

internal static class ClusterWireCases
{
    public static readonly UniqueAddress First = new(new Address("akka.tcp", "cluster", "node1", 2552), (1L << 40) + 17);
    public static readonly UniqueAddress Second = new(new Address("akka.tcp", "cluster", "node2", 2553), long.MaxValue - 1);
    public static readonly UniqueAddress Removed = new(Second.Address, long.MaxValue - 2);

    public static Gossip CreateGossip() => new(
        ImmutableSortedSet.Create(Member.Ordering,
            Member.Create(First, 7, MemberStatus.Up, ImmutableHashSet.Create("backend"), AppVersion.Create("1.2.3")),
            Member.Create(Second, 11, MemberStatus.Leaving, ImmutableHashSet.Create("backend"), AppVersion.Create("1.2.3"))),
        new GossipOverview(ImmutableHashSet.Create(First), new Reachability(
            ImmutableList.Create(new Reachability.Record(First, Second, Reachability.ReachabilityStatus.Unreachable, 19)),
            ImmutableDictionary<UniqueAddress, long>.Empty.Add(First, 23))),
        VectorClock.Create(ImmutableSortedDictionary<VectorClock.Node, long>.Empty.Add(VectorClock.Node.FromHash("fixed-hash"), 29)),
        ImmutableDictionary<UniqueAddress, long>.Empty.Add(Removed, 123456789));

    public static (object Message, string Manifest, string Name)[] Create(bool legacyHeartbeat = false) =>
    [
        (new InternalClusterAction.Join(First, ImmutableHashSet.Create("backend"), AppVersion.Create("1.2.3")), ClusterMessageSerializer.JoinManifest, "Join"),
        (new InternalClusterAction.Join(First, ImmutableHashSet<string>.Empty, AppVersion.Zero), ClusterMessageSerializer.JoinManifest, "JoinEmpty"),
        (new InternalClusterAction.Welcome(First, CreateGossip()), ClusterMessageSerializer.WelcomeManifest, "Welcome"),
        (new InternalClusterAction.Welcome(First, Gossip.Empty), ClusterMessageSerializer.WelcomeManifest, "WelcomeEmpty"),
        (new ClusterUserAction.Leave(First.Address), ClusterMessageSerializer.LeaveManifest, "Leave"),
        (new ClusterUserAction.Down(First.Address), ClusterMessageSerializer.DownManifest, "Down"),
        (new InternalClusterAction.InitJoin(), ClusterMessageSerializer.InitJoinManifest, "InitJoin"),
        (new InternalClusterAction.InitJoinAck(First.Address), ClusterMessageSerializer.InitJoinAckManifest, "InitJoinAck"),
        (new InternalClusterAction.InitJoinNack(First.Address), ClusterMessageSerializer.InitJoinNackManifest, "InitJoinNack"),
        (new ClusterHeartbeatSender.Heartbeat(First.Address, legacyHeartbeat ? -1 : 17, legacyHeartbeat ? -1 : 1234),
            legacyHeartbeat ? ClusterMessageSerializer.HeartBeatManifestPre1419 : ClusterMessageSerializer.HeartBeatManifest, "Heartbeat"),
        (new ClusterHeartbeatSender.HeartbeatRsp(First, legacyHeartbeat ? -1 : 17, legacyHeartbeat ? -1 : 1234),
            legacyHeartbeat ? ClusterMessageSerializer.HeartBeatRspManifestPre1419 : ClusterMessageSerializer.HeartBeatRspManifest, "HeartbeatRsp"),
        (new InternalClusterAction.ExitingConfirmed(First), ClusterMessageSerializer.ExitingConfirmedManifest, "ExitingConfirmed"),
        (new GossipStatus(First, CreateGossip().Version), ClusterMessageSerializer.GossipStatusManifest, "GossipStatus"),
        (new GossipStatus(First, VectorClock.Create()), ClusterMessageSerializer.GossipStatusManifest, "GossipStatusEmpty"),
        (new GossipEnvelope(First, Second, CreateGossip()), ClusterMessageSerializer.GossipEnvelopeManifest, "GossipEnvelope"),
        (new GossipEnvelope(First, Second, Gossip.Empty), ClusterMessageSerializer.GossipEnvelopeManifest, "GossipEnvelopeEmpty"),
        (new ClusterRouterPool(new RoundRobinPool(4), new ClusterRouterPoolSettings(13, 5, true, "backend")), ClusterMessageSerializer.ClusterRouterPoolManifest, "RouterPool"),
        (new ClusterRouterPool(new RoundRobinPool(4), new ClusterRouterPoolSettings(13, 5, false, null!)), ClusterMessageSerializer.ClusterRouterPoolManifest, "RouterPoolNoRole")
    ];
}
