//-----------------------------------------------------------------------
// <copyright file="DDataSerializationBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster.Sharding;
using Akka.Actor.Dsl;
using Akka.Cluster.Sharding.Serialization.Proto.Msg;
using Akka.Cluster.Tests;
using Akka.Configuration;
using Akka.DistributedData;
using Akka.DistributedData.Internal;
using Akka.DistributedData.Serialization;
using Akka.Util;
using BenchmarkDotNet.Attributes;

namespace Akka.Cluster.Benchmarks.Serialization;

[Config(typeof(MicroBenchmarkConfig))]
public class DDataShardCoordinatorStateSerializationBenchmarks
{
    private static readonly Config BaseConfig = ConfigurationFactory.ParseString("""
                akka.actor.provider=cluster
                akka.remote.dot-netty.tcp.port = 0
        """).WithFallback(ClusterSharding.DefaultConfig());
    private ExtendedActorSystem _system;
    private ReplicatedDataSerializer _replicatedDataSerializer;
    private ReplicatorMessageSerializer _replicatorMessageSerializer;
    
    private static readonly Member FakeNode = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up,
        ImmutableHashSet.Create("r1"), appVersion: AppVersion.Create("1.1.0"));
    
    [Params(1, 20, 100, 1000)]
    public int ShardCount { get; set; }


    public int RegionCount => Math.Max(ShardCount / 10, 1);
    
    // Used to represent shards and regions
    private IActorRef[] _placeHolders;
    
    private readonly LWWRegisterKey<ShardCoordinator.CoordinatorState> _coordinatorStateKey = new("CoordinatorState");
    
    private LWWRegister<ShardCoordinator.CoordinatorState> _coordinatorState;
    private byte[] _serializedCoordinatorState;
    private string _lwwRegisterManifest = string.Empty;

    private DataEnvelope _dataEnvelope;
    private byte[] _serializedDataEnvelope;
    private string _dataEnvelopeManifest = string.Empty;

    private Write _dataWrite;
    private byte[] _serializedDataWrite;
    private string _dataWriteManifest = string.Empty;
    

    private static ShardCoordinator.CoordinatorState ComputedState(IActorRef[] placeholders, int shardCount,
        int regionCount)
    {
        var shards = Enumerable.Range(0, shardCount)
            .Select(i => new KeyValuePair<string, IActorRef>($"shard-{i}", placeholders[0]))
            .ToImmutableDictionary( );
        
        // evenly allocate shards to regions
        var shardsPerRegionCount = shardCount / regionCount; // region count can't be zero
        var shardsPerRegionBuilder = ImmutableDictionary.CreateBuilder<IActorRef, IImmutableList<string>>();
        var i = 0;
        foreach (var chunk in shards
                     .Chunk(shardsPerRegionCount))
        {
            var region = placeholders[i];
            shardsPerRegionBuilder.Add(region, chunk.Select(kv => kv.Key).ToImmutableList());
            i++;
        }

        var regionProxies = ImmutableHashSet<IActorRef>.Empty;
        var unallocatedShards = ImmutableHashSet<string>.Empty;
        
        return new ShardCoordinator.CoordinatorState(shards, shardsPerRegionBuilder.ToImmutable(), regionProxies, unallocatedShards);
    }
    
    [GlobalSetup]
    public void Setup()
    {
        _system ??= (ExtendedActorSystem)ActorSystem.Create("system", BaseConfig);
        _placeHolders ??= Enumerable.Range(0, RegionCount).Select(c => _system.ActorOf(ctx => { })).ToArray();
        _replicatedDataSerializer = new ReplicatedDataSerializer(_system);
        _replicatorMessageSerializer = new ReplicatorMessageSerializer(_system);
        _coordinatorState = new LWWRegister<ShardCoordinator.CoordinatorState>(FakeNode.UniqueAddress, 
            ComputedState(_placeHolders, ShardCount, RegionCount));
        
        // LWWRegister
        
        _serializedCoordinatorState = _replicatedDataSerializer.ToBinary(_coordinatorState);
        _lwwRegisterManifest = _replicatedDataSerializer.Manifest(_coordinatorState);
        
        // DataEnvelope
        _dataEnvelope = new DataEnvelope(_coordinatorState);
        _serializedDataEnvelope = _replicatorMessageSerializer.ToBinary(_dataEnvelope);
        _dataEnvelopeManifest = _replicatorMessageSerializer.Manifest(_dataEnvelope);
        
        // Write
        _dataWrite = new Write(_coordinatorStateKey.Id, _dataEnvelope, FakeNode.UniqueAddress);
        _serializedDataWrite = _replicatorMessageSerializer.ToBinary(_dataWrite);
        _dataWriteManifest = _replicatorMessageSerializer.Manifest(_dataWrite);
    }

    /// <summary>
    /// Serialize the raw LWWRegister payload
    /// </summary>
    [Benchmark]
    public byte[] SerializeCoordinatorState()
    {
        return _replicatedDataSerializer.ToBinary(_coordinatorState);
    }
    
    [Benchmark]
    public object DeserializeCoordinatorState()
    {
        return _replicatedDataSerializer.FromBinary(_serializedCoordinatorState, _lwwRegisterManifest);
    } 
    
    [Benchmark]
    public byte[] SerializeDataEnvelope()
    {
        return _replicatorMessageSerializer.ToBinary(_dataEnvelope);
    }
    
    [Benchmark]
    public object DeserializeDataEnvelope()
    {
        return _replicatorMessageSerializer.FromBinary(_serializedDataEnvelope, _dataEnvelopeManifest);
    }
    
    [Benchmark]
    public byte[] SerializeDataWrite()
    {
        return _replicatorMessageSerializer.ToBinary(_dataWrite);
    }
    
    [Benchmark]
    public object DeserializeDataWrite()
    {
        return _replicatorMessageSerializer.FromBinary(_serializedDataWrite, _dataWriteManifest);
    }
    
    [GlobalCleanup]
    public void Cleanup()
    {
        _system.Dispose();
    }
}
