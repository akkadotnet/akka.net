// -----------------------------------------------------------------------
//  <copyright file="ClusterMessageSerializerBenchmarks.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Serialization;
using Akka.Cluster.Tests;
using Akka.Util;
using BenchmarkDotNet.Attributes;

namespace Akka.Cluster.Benchmarks.Serialization;

public sealed class ClusterMessageSerializerBenchmarks
{
    private ExtendedActorSystem system;
    private ClusterMessageSerializer clusterMessageSerializer;

    private static readonly Member a1 = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552),
        MemberStatus.Joining, appVersion: AppVersion.Create("1.0.0"));

    private static readonly Member b1 = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up,
        ImmutableHashSet.Create("r1"), appVersion: AppVersion.Create("1.1.0"));

    private static readonly Member c1 = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552),
        MemberStatus.Leaving, ImmutableHashSet.Create("r2"), appVersion: AppVersion.Create("1.1.0"));

    private static readonly Member d1 = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552),
        MemberStatus.Exiting, ImmutableHashSet.Create("r1", "r2"));

    private static readonly Member e1 = TestMember.Create(new Address("akka.tcp", "sys", "e", 2552), MemberStatus.Down,
        ImmutableHashSet.Create("r3"));

    [GlobalSetup]
    public void Setup()
    {
        system = (ExtendedActorSystem)ActorSystem.Create("system", "akka.actor.provider=cluster");
        clusterMessageSerializer = new ClusterMessageSerializer(system);
    }

    private static readonly ClusterHeartbeatSender.Heartbeat heartbeat =
        new ClusterHeartbeatSender.Heartbeat(a1.UniqueAddress.Address, 10, 3);

    private static readonly ClusterHeartbeatSender.HeartbeatRsp heartbeatRsp =
        new ClusterHeartbeatSender.HeartbeatRsp(b1.UniqueAddress, 10, 3);

    [Benchmark]
    public byte[] Serialize_Heartbeat()
    {
        return clusterMessageSerializer.ToBinary(heartbeat);
    }

    [Benchmark]
    public object Deserialize_Heartbeat()
    {
        return clusterMessageSerializer.FromBinary(clusterMessageSerializer.ToBinary(heartbeat),
            clusterMessageSerializer.Manifest(heartbeat));
    }

    [Benchmark]
    public byte[] Serialize_HeartbeatRsp()
    {
        return clusterMessageSerializer.ToBinary(heartbeatRsp);
    }

    [Benchmark]
    public object Deserialize_HeartbeatRsp()
    {
        return clusterMessageSerializer.FromBinary(clusterMessageSerializer.ToBinary(heartbeatRsp),
            clusterMessageSerializer.Manifest(heartbeatRsp));
    }

    private static readonly GossipEnvelope _gossipEnvelope = new GossipEnvelope(a1.UniqueAddress, c1.UniqueAddress,
        new Gossip(ImmutableSortedSet.Create(a1, b1, c1, d1)).Increment(new VectorClock.Node("node1"))
            .Increment(new VectorClock.Node("node2"))
            .Seen(a1.UniqueAddress)
            .Seen(b1.UniqueAddress));
    private static readonly Gossip _gossip2 = _gossipEnvelope.Gossip
        .Increment(new VectorClock.Node("node3"))
        .Increment(new VectorClock.Node("node4"))
        .Seen(a1.UniqueAddress).Seen(c1.UniqueAddress);
    private static readonly Reachability _reachability = Reachability.Empty.Unreachable(a1.UniqueAddress, e1.UniqueAddress).Unreachable(b1.UniqueAddress, e1.UniqueAddress);

    private static readonly Gossip _gossip3 = _gossip2.Copy(ImmutableSortedSet.Create(a1, b1, c1, d1, e1),
        overview: _gossip2.Overview.Copy(reachability: _reachability));
    
    private static readonly GossipStatus _gossipStatus = new GossipStatus(a1.UniqueAddress, _gossip3.Version);
    private static readonly InternalClusterAction.Welcome _welcome = new InternalClusterAction.Welcome(a1.UniqueAddress, _gossip3);
    
    [Benchmark]
    public byte[] Serialize_GossipEnvelope()
    {
        return clusterMessageSerializer.ToBinary(_gossipEnvelope);
    }
    
    [Benchmark]
    public object Deserialize_GossipEnvelope()
    {
        return clusterMessageSerializer.FromBinary(clusterMessageSerializer.ToBinary(_gossipEnvelope),
            clusterMessageSerializer.Manifest(_gossipEnvelope));
    }
    
    [Benchmark]
    public byte[] Serialize_GossipStatus()
    {
        return clusterMessageSerializer.ToBinary(_gossipStatus);
    }
    
    [Benchmark]
    public object Deserialize_GossipStatus()
    {
        return clusterMessageSerializer.FromBinary(clusterMessageSerializer.ToBinary(_gossipStatus),
            clusterMessageSerializer.Manifest(_gossipStatus));
    }
    
    [Benchmark]
    public byte[] Serialize_Welcome()
    {
        return clusterMessageSerializer.ToBinary(_welcome);
    }
    
    [Benchmark]
    public object Deserialize_Welcome()
    {
        return clusterMessageSerializer.FromBinary(clusterMessageSerializer.ToBinary(_welcome),
            clusterMessageSerializer.Manifest(_welcome));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        system.Dispose();
    }
}