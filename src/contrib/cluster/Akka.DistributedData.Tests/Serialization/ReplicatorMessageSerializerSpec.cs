//-----------------------------------------------------------------------
// <copyright file="ReplicatorMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Internal;
using Akka.IO;
using Xunit;
using Xunit.Abstractions;
using Address = Akka.Actor.Address;
using UniqueAddress = Akka.Cluster.UniqueAddress;

namespace Akka.DistributedData.Tests.Serialization
{
    [Collection("DistributedDataSpec")]
    public class ReplicatorMessageSerializerSpec : TestKit.Xunit2.TestKit
    {
        private static readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
            akka.actor {
                provider=""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                serializers {
                  wire = ""Akka.Serialization.WireSerializer, Akka.Serialization.Wire""
                }
                serialization-bindings {
                  ""Akka.DistributedData.IReplicatorMessage, Akka.DistributedData"" = wire
                }
            }
            akka.remote.helios.tcp.port = 0").WithFallback(DistributedData.DefaultConfig());
        
        private readonly UniqueAddress _address1 = new UniqueAddress(new Address("akka.tcp", "sys", "some.host.org", 4711), 1);
        private readonly UniqueAddress _address2 = new UniqueAddress(new Address("akka.tcp", "sys", "other.host.org", 4711), 2);
        private readonly UniqueAddress _address3 = new UniqueAddress(new Address("akka.tcp", "sys", "some.host.org", 4711), 3);

        private readonly GSetKey<string> _keyA = new GSetKey<string>("A");

        public ReplicatorMessageSerializerSpec(ITestOutputHelper output) : base(BaseConfig, "ReplicatorMessageSerializerSpec", output)
        {
        }

        [Fact]
        public void ReplicatorMessageSerializer_should_serialize_Replicator_message()
        {
            var ref1 = Sys.ActorOf(Props.Empty, "ref1");
            var data1 = GSet.Create("a");

            CheckSerialization(new Replicator.Get(_keyA, ReadLocal.Instance));
            CheckSerialization(new Replicator.Get(_keyA, new ReadMajority(TimeSpan.FromSeconds(2)), "x"));
            CheckSerialization(new Replicator.GetSuccess(_keyA, null, data1));
            CheckSerialization(new Replicator.GetSuccess(_keyA, "x", data1));
            CheckSerialization(new Replicator.NotFound(_keyA, "x"));
            CheckSerialization(new Replicator.GetFailure(_keyA, "x"));
            CheckSerialization(new Replicator.Subscribe(_keyA, ref1));
            CheckSerialization(new Replicator.Unsubscribe(_keyA, ref1));
            CheckSerialization(new Replicator.Changed(_keyA, data1));
            CheckSerialization(new DataEnvelope(data1));
            CheckSerialization(new DataEnvelope(data1, ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<UniqueAddress, PruningState>(_address1, new PruningState(_address2, PruningPerformed.Instance)), 
                new KeyValuePair<UniqueAddress, PruningState>(_address3, new PruningState(_address2, new PruningInitialized(_address1.Address))), 
            })));
            CheckSerialization(new Write("A", new DataEnvelope(data1)));
            CheckSerialization(WriteAck.Instance);
            CheckSerialization(new Read("A"));
            CheckSerialization(new ReadResult(new DataEnvelope(data1)));
            CheckSerialization(new ReadResult(null));
            CheckSerialization(new Internal.Status(ImmutableDictionary.CreateRange(new []
            {
                new KeyValuePair<string, ByteString>("A", ByteString.FromString("a")),
                new KeyValuePair<string, ByteString>("B", ByteString.FromString("b")),  
            }), 3, 10));
            CheckSerialization(new Gossip(ImmutableDictionary.CreateRange(new []
            {
                new KeyValuePair<string, DataEnvelope>("A", new DataEnvelope(data1)),
                new KeyValuePair<string, DataEnvelope>("B", new DataEnvelope(GSet.Create("b").Add("b"))),  
            }), true));
        }

        private void CheckSerialization(object expected)
        {
            var serializer = Sys.Serialization.FindSerializerFor(expected);
            var blob = serializer.ToBinary(expected);
            var actual = serializer.FromBinary(blob, expected.GetType());

            Assert.True(expected.Equals(actual), $"Expected: {expected}\nActual: {actual}");
        }
    }
}
