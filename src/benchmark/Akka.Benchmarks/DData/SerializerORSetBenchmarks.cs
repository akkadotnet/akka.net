//-----------------------------------------------------------------------
// <copyright file="SerializerORSetBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Benchmarks.Configurations;
using Akka.Cluster;
using Akka.Configuration;
using Akka.DistributedData;
using Akka.DistributedData.Serialization;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.DData;

[Config(typeof(MicroBenchmarkConfig))]
public class SerializerORSetBenchmarks
{
    [Params(25)] 
    public int NumElements;

    [Params(10)]
    public int NumNodes;
        
    private ActorSystem sys;
    private ReplicatedDataSerializer ser;
    private UniqueAddress[] _nodes;
    private RDDBenchTypes.TestVal[] _elements;
    private ORSet<List<RDDBenchTypes.TestVal>> _c1;
    private byte[] _c1Ser;
    private string _c1Manifest;

    [GlobalSetup]
    public void SetupSystem()
    {
        var newNodes = new List<UniqueAddress>(NumNodes);
        foreach(var i in Enumerable.Range(0, NumNodes)){
            var address = new Address("akka.tcp", "Sys", "localhost", 2552 + i);
            var uniqueAddress = new UniqueAddress(address, i);
            newNodes.Add(uniqueAddress);
        }
        _nodes = newNodes.ToArray();
        var newElements = new List<RDDBenchTypes.TestVal>(NumNodes);
        foreach(var i in Enumerable.Range(0, NumElements)){
            newElements.Add(new RDDBenchTypes.TestVal(i.ToString()));
        }
        _elements = newElements.ToArray();

        _c1 = ORSet<List<RDDBenchTypes.TestVal>>.Empty;
        foreach(var node in _nodes){
            _c1 = _c1.Add(node, _elements.ToList());
        }

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
    [Benchmark]
    public void Serialize_ORSet()
    {
        ser.ToBinary(_c1);
    }

    [Benchmark]
    public void Deserialize_ORSet()
    {
        ser.FromBinary(_c1Ser, _c1Manifest);
    }

}
