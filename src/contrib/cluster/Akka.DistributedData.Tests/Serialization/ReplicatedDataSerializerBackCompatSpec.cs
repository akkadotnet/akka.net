// //-----------------------------------------------------------------------
// // <copyright file="ReplicatedDataSerializerBackCompatSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.DistributedData.Serialization;
using Akka.DistributedData.Serialization.Proto.Msg;
using FluentAssertions;
using Xunit;
using UniqueAddress = Akka.Cluster.UniqueAddress;
using static FluentAssertions.FluentActions;

namespace Akka.DistributedData.Tests.Serialization;

public class ReplicatedDataSerializerBackCompatSpec
{
    private readonly ReplicatedDataSerializer _serializer;
    private readonly UniqueAddress _address;
    
    public ReplicatedDataSerializerBackCompatSpec()
    {
        var sys = ActorSystem.Create("test", @"
akka.actor.provider = cluster
akka.cluster.sharding.distributed-data.backward-compatible-wire-format = true");
        _serializer = new ReplicatedDataSerializer((ExtendedActorSystem)sys);
        _address = Cluster.Cluster.Get(sys).SelfUniqueAddress;
        sys.Terminate();
    }

    [Fact(DisplayName = "DData replicated data serializer should serialize and deserialize correct backward compatible proto message")]
    public void SerializeTest()
    {
        var lwwReg = new LWWRegister<ShardCoordinator.CoordinatorState>(_address, ShardCoordinator.CoordinatorState.Empty);
        var bytes = _serializer.ToBinary(lwwReg);
        var proto = LWWRegister.Parser.ParseFrom(bytes);
        
        // Serialized type name should be equal to the old v1.4 coordinator state FQCN
        proto.TypeInfo.TypeName.Should().Be("Akka.Cluster.Sharding.PersistentShardCoordinator+State, Akka.Cluster.Sharding");
        
        // Deserializing the same message should succeed
        Invoking(() => _serializer.FromBinary(bytes, _serializer.Manifest(lwwReg)))
            .Should().NotThrow()
            .And.Subject().Should().BeOfType<LWWRegister<ShardCoordinator.CoordinatorState>>();
    }
}