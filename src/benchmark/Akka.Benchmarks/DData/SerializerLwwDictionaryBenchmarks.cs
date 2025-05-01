//-----------------------------------------------------------------------
// <copyright file="SerializerLwwDictionaryBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster;
using Akka.Configuration;
using Akka.DistributedData;
using Akka.DistributedData.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.DData;

[Config(typeof(MicroBenchmarkConfig))]
public class SerializerLwwDictionaryBenchmarks
{
    [Params(typeof(RDDBenchTypes.TestKey), typeof(RDDBenchTypes.TestVal))]
    public Type KeyType;

    [Params(typeof(RDDBenchTypes.TestKey), typeof(RDDBenchTypes.TestVal))]
    public Type ValueType;
    
    [Params(25)] 
    public int NumElements;

    [Params(10)]
    public int NumNodes;

    private UniqueAddress[] _nodes;
    private object _c1;
    private ActorSystem sys;
    private ReplicatedDataSerializer ser;
    private byte[] _c1Ser;
    private string _c1Manifest;

    [GlobalSetup]
    public void SetupSystem()
    {
        typeof(SerializerLwwDictionaryBenchmarks).GetMethod(
                nameof(SerializerLwwDictionaryBenchmarks.CreateItems),
                BindingFlags.Instance | BindingFlags.NonPublic)
            .MakeGenericMethod(new []{KeyType,ValueType})
            .Invoke(this, new object[]{});
        var conf = ConfigurationFactory.ParseString(@"akka.actor {
  serializers {
    akka-replicated-data = ""Akka.DistributedData.Serialization.ReplicatedDataSerializer, Akka.DistributedData""
  }
  serialization-bindings {
    ""Akka.DistributedData.IReplicatedDataSerialization, Akka.DistributedData"" = akka-replicated-data
  }
  serialization-identifiers {
	""Akka.DistributedData.Serialization.ReplicatedDataSerializer, Akka.DistributedData"" = 11
  }
}");
        sys = ActorSystem.Create("rddsb", conf);
        ser = (ReplicatedDataSerializer)sys.Serialization.FindSerializerForType(
            typeof(IReplicatedDataSerialization));
        _c1Ser = ser.ToBinary(_c1);
        _c1Manifest = ser.Manifest(_c1);
    }

    private void CreateItems<TKey,TValue>()
    {
        var newNodes = new List<UniqueAddress>(NumNodes);
        foreach (var i in Enumerable.Range(0, NumNodes))
        {
            var address = new Address("akka.tcp", "Sys", "localhost", 2552 + i);
            var uniqueAddress = new UniqueAddress(address, i);
            newNodes.Add(uniqueAddress);
        }

        _nodes = newNodes.ToArray();
        var newElements = new List<TValue>(NumNodes);
        foreach (var i in Enumerable.Range(0, NumElements))
        {
            
                newElements.Add(generate<TValue>(i));
        }

        var _c1 = LWWDictionary<TKey, List<TValue>>
            .Empty;
        int j = 0;
        foreach (var node in _nodes)
        {
            _c1 = _c1.SetItem(node, generate<TKey>(j),
                newElements);
            j++;
        }

        this._c1 = _c1;
    }

    private TValue generate<TValue>(int i)
    {
        if (typeof(TValue) == typeof(RDDBenchTypes.TestVal))
        {
            return (TValue)(object)new RDDBenchTypes.TestVal(i.ToString());
        }
        else if (typeof(TValue) == typeof(RDDBenchTypes.TestKey))
        {
            return (TValue)(object)new RDDBenchTypes.TestKey(i);
        }
        else if (typeof(TValue) == typeof(int))
        {
            return (TValue)(object)i;
        }
        else if (typeof(TValue) == typeof(string))
        {
            return (TValue)(object)i.ToString();
        }
        else if (typeof(TValue) == typeof(long))
        {
            return (TValue)(object)(i);
        }
        else
        {
            return (TValue)(object)(i);
        }
    }

    [Benchmark]
    public void Serialize_LWWDict()
    {
        ser.ToBinary(_c1);
    }

    [Benchmark]
    public void Deserialize_LWWDict()
    {
        ser.FromBinary(_c1Ser, _c1Manifest);
    }
}
