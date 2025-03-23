//-----------------------------------------------------------------------
// <copyright file="ClusterMessageSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
        _system ??= (ExtendedActorSystem)ActorSystem.Create("system", "akka.actor.provider=cluster");
        _clusterMessageSerializer ??= new ClusterMessageSerializer(_system);
    }

    private static readonly ClusterHeartbeatSender.Heartbeat Heartbeat = new(A1.UniqueAddress.Address, 10, 3);

    private static readonly ClusterHeartbeatSender.HeartbeatRsp HeartbeatRsp = new(B1.UniqueAddress, 10, 3);

    [Benchmark]
    public byte[] Serialize_Heartbeat()
    {
        return _clusterMessageSerializer.ToBinary(Heartbeat);
    }

    private static byte[] _serializedHeartbeat = Array.Empty<byte>();
    private static string _heartbeatManifest = string.Empty;
    
    [GlobalSetup(Target = nameof(Deserialize_Heartbeat))]
    public void SetupSerializedHeartbeat()
    {
        Setup();
        if(_serializedHeartbeat.Length == 0)
            _serializedHeartbeat = _clusterMessageSerializer.ToBinary(Heartbeat);
        if(string.IsNullOrEmpty(_heartbeatManifest))
            _heartbeatManifest = _clusterMessageSerializer.Manifest(Heartbeat);
    }

    [Benchmark]
    public object Deserialize_Heartbeat()
    {
        return _clusterMessageSerializer.FromBinary(_serializedHeartbeat, _heartbeatManifest);
    }

    [Benchmark]
    public byte[] Serialize_HeartbeatRsp()
    {
        return _clusterMessageSerializer.ToBinary(HeartbeatRsp);
    }

    private static byte[] _serializedHeartbeatRsp = Array.Empty<byte>();
    private static string _heartbeatRspManifest = string.Empty;
    
    [GlobalSetup(Target = nameof(Deserialize_HeartbeatRsp))]
    public void SetupSerializedHeartbeatRsp()
    {
        Setup();
        if(_serializedHeartbeatRsp.Length == 0)
            _serializedHeartbeatRsp = _clusterMessageSerializer.ToBinary(HeartbeatRsp);
        if(string.IsNullOrEmpty(_heartbeatRspManifest))
            _heartbeatRspManifest = _clusterMessageSerializer.Manifest(HeartbeatRsp);
    }
    
    [Benchmark]
    public object Deserialize_HeartbeatRsp()
    {
        return _clusterMessageSerializer.FromBinary(_serializedHeartbeatRsp, _heartbeatRspManifest);
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
    
    private static byte[] _serializedGossipEnvelope = Array.Empty<byte>();
    private static string _gossipEnvelopeManifest = string.Empty;
    
    [GlobalSetup(Target = nameof(Deserialize_GossipEnvelope))]
    public void SetupSerializedGossipEnvelope()
    {
        Setup();
        if(_serializedGossipEnvelope.Length == 0)
            _serializedGossipEnvelope = _clusterMessageSerializer.ToBinary(GossipEnvelope);
        if(string.IsNullOrEmpty(_gossipEnvelopeManifest))
            _gossipEnvelopeManifest = _clusterMessageSerializer.Manifest(GossipEnvelope);
    }
    
    [Benchmark]
    public object Deserialize_GossipEnvelope()
    {
        return _clusterMessageSerializer.FromBinary(_serializedGossipEnvelope, _gossipEnvelopeManifest);
    }
    
    [Benchmark]
    public byte[] Serialize_GossipStatus()
    {
        return _clusterMessageSerializer.ToBinary(GossipStatus);
    }
    
    private static byte[] _serializedGossipStatus = Array.Empty<byte>();
    private static string _gossipStatusManifest = string.Empty;
    
    [GlobalSetup(Target = nameof(Deserialize_GossipStatus))]
    public void SetupSerializedGossipStatus()
    {
        Setup();
        if(_serializedGossipStatus.Length == 0)
            _serializedGossipStatus = _clusterMessageSerializer.ToBinary(GossipStatus);
        if(string.IsNullOrEmpty(_gossipStatusManifest))
            _gossipStatusManifest = _clusterMessageSerializer.Manifest(GossipStatus);
    }
    
    [Benchmark]
    public object Deserialize_GossipStatus()
    {
        return _clusterMessageSerializer.FromBinary(_serializedGossipStatus, _gossipStatusManifest);
    }
    
    [Benchmark]
    public byte[] Serialize_Welcome()
    {
        return _clusterMessageSerializer.ToBinary(Welcome);
    }
    
    private static byte[] _serializedWelcome = Array.Empty<byte>();
    private static string _welcomeManifest = string.Empty;
    
    [GlobalSetup(Target = nameof(Deserialize_Welcome))]
    public void SetupSerializedWelcome()
    {
        Setup();
        if(_serializedWelcome.Length == 0)
            _serializedWelcome = _clusterMessageSerializer.ToBinary(Welcome);
        if(string.IsNullOrEmpty(_welcomeManifest))
            _welcomeManifest = _clusterMessageSerializer.Manifest(Welcome);
    }
    
    [Benchmark]
    public object Deserialize_Welcome()
    {
        return _clusterMessageSerializer.FromBinary(_serializedWelcome, _welcomeManifest);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _system.Dispose();
    }
}
