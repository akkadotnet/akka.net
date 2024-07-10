// -----------------------------------------------------------------------
//  <copyright file="ClusterMessageSerializerBenchmarks.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster.Serialization;
using Akka.Cluster.Tests;
using Akka.Util;
using BenchmarkDotNet.Attributes;

namespace Akka.Cluster.Benchmarks.Serialization;

[Config(typeof(MicroBenchmarkConfig))]
public class ClusterMessageSerializerBenchmarks
{
    private ExtendedActorSystem _system;
    private ClusterMessageSerializer _clusterMessageSerializer;

    private static readonly Member A1 = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552),
        MemberStatus.Joining, appVersion: AppVersion.Create("1.0.0"));

    private static readonly Member B1 = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up,
        ImmutableHashSet.Create("r1"), appVersion: AppVersion.Create("1.1.0"));

    private static readonly Member C1 = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552),
        MemberStatus.Leaving, ImmutableHashSet.Create("r2"), appVersion: AppVersion.Create("1.1.0"));

    private static readonly Member D1 = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552),
        MemberStatus.Exiting, ImmutableHashSet.Create("r1", "r2"));

    private static readonly Member E1 = TestMember.Create(new Address("akka.tcp", "sys", "e", 2552), MemberStatus.Down,
        ImmutableHashSet.Create("r3"));

    [GlobalSetup]
    public void Setup()
    {
        _system = (ExtendedActorSystem)ActorSystem.Create("system", "akka.actor.provider=cluster");
        _clusterMessageSerializer = new ClusterMessageSerializer(_system);
    }

    private static readonly ClusterHeartbeatSender.Heartbeat Heartbeat = new(A1.UniqueAddress.Address, 10, 3);

    private static readonly ClusterHeartbeatSender.HeartbeatRsp HeartbeatRsp = new(B1.UniqueAddress, 10, 3);

    [Benchmark]
    public byte[] Serialize_Heartbeat()
    {
        return _clusterMessageSerializer.ToBinary(Heartbeat);
    }

    [Benchmark]
    public object Deserialize_Heartbeat()
    {
        return _clusterMessageSerializer.FromBinary(_clusterMessageSerializer.ToBinary(Heartbeat),
            _clusterMessageSerializer.Manifest(Heartbeat));
    }

    [Benchmark]
    public byte[] Serialize_HeartbeatRsp()
    {
        return _clusterMessageSerializer.ToBinary(HeartbeatRsp);
    }

    [Benchmark]
    public object Deserialize_HeartbeatRsp()
    {
        return _clusterMessageSerializer.FromBinary(_clusterMessageSerializer.ToBinary(HeartbeatRsp),
            _clusterMessageSerializer.Manifest(HeartbeatRsp));
    }

    private static readonly GossipEnvelope GossipEnvelope = new(A1.UniqueAddress, C1.UniqueAddress,
        new Gossip(ImmutableSortedSet.Create(A1, B1, C1, D1)).Increment(new VectorClock.Node("node1"))
            .Increment(new VectorClock.Node("node2"))
            .Seen(A1.UniqueAddress)
            .Seen(B1.UniqueAddress));
    private static readonly Gossip Gossip2 = GossipEnvelope.Gossip
        .Increment(new VectorClock.Node("node3"))
        .Increment(new VectorClock.Node("node4"))
        .Seen(A1.UniqueAddress).Seen(C1.UniqueAddress);
    private static readonly Reachability Reachability = Reachability.Empty.Unreachable(A1.UniqueAddress, E1.UniqueAddress).Unreachable(B1.UniqueAddress, E1.UniqueAddress);

    private static readonly Gossip Gossip3 = Gossip2.Copy(ImmutableSortedSet.Create(A1, B1, C1, D1, E1),
        overview: Gossip2.Overview.Copy(reachability: Reachability));
    
    private static readonly GossipStatus GossipStatus = new(A1.UniqueAddress, Gossip3.Version);
    private static readonly InternalClusterAction.Welcome Welcome = new(A1.UniqueAddress, Gossip3);
    
    [Benchmark]
    public byte[] Serialize_GossipEnvelope()
    {
        return _clusterMessageSerializer.ToBinary(GossipEnvelope);
    }
    
    [Benchmark]
    public object Deserialize_GossipEnvelope()
    {
        return _clusterMessageSerializer.FromBinary(_clusterMessageSerializer.ToBinary(GossipEnvelope),
            _clusterMessageSerializer.Manifest(GossipEnvelope));
    }
    
    [Benchmark]
    public byte[] Serialize_GossipStatus()
    {
        return _clusterMessageSerializer.ToBinary(GossipStatus);
    }
    
    [Benchmark]
    public object Deserialize_GossipStatus()
    {
        return _clusterMessageSerializer.FromBinary(_clusterMessageSerializer.ToBinary(GossipStatus),
            _clusterMessageSerializer.Manifest(GossipStatus));
    }
    
    [Benchmark]
    public byte[] Serialize_Welcome()
    {
        return _clusterMessageSerializer.ToBinary(Welcome);
    }
    
    [Benchmark]
    public object Deserialize_Welcome()
    {
        return _clusterMessageSerializer.FromBinary(_clusterMessageSerializer.ToBinary(Welcome),
            _clusterMessageSerializer.Manifest(Welcome));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _system.Dispose();
    }
}