//-----------------------------------------------------------------------
// <copyright file="ReplicatorMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Internal;
using Akka.Serialization;
using Google.Protobuf;
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
                provider=cluster
            }
            akka.remote.dot-netty.tcp.port = 0").WithFallback(DistributedData.DefaultConfig());

        private readonly UniqueAddress _address1;
        private readonly UniqueAddress _address2;
        private readonly UniqueAddress _address3;

        private readonly GSetKey<string> _keyA = new GSetKey<string>("A");

        public ReplicatorMessageSerializerSpec(ITestOutputHelper output) : base(BaseConfig, "ReplicatorMessageSerializerSpec", output)
        {
            _address1 = new UniqueAddress(new Address("akka.tcp", Sys.Name, "some.host.org", 4711), 1);
            _address2 = new UniqueAddress(new Address("akka.tcp", Sys.Name, "other.host.org", 4711), 2);
            _address3 = new UniqueAddress(new Address("akka.tcp", Sys.Name, "some.host.org", 4711), 3);
        }

        [Fact]
        public void ReplicatorMessageSerializer_should_serialize_Replicator_message()
        {
            var ref1 = Sys.ActorOf(Props.Empty, "ref1");
            var data1 = GSet.Create("a");

            CheckSerialization(new Get<GSet<string>>(_keyA, ReadLocal.Instance));
            CheckSerialization(new Get<GSet<string>>(_keyA, new ReadMajority(TimeSpan.FromSeconds(2)), "x"));
            CheckSerialization(new GetSuccess<GSet<string>>(_keyA, null, data1));
            CheckSerialization(new GetSuccess<GSet<string>>(_keyA, "x", data1));
            CheckSerialization(new NotFound<GSet<string>>(_keyA, "x"));
            CheckSerialization(new GetFailure<GSet<string>>(_keyA, "x"));
            CheckSerialization(new Subscribe<GSet<string>>(_keyA, ref1));
            CheckSerialization(new Unsubscribe<GSet<string>>(_keyA, ref1));
            CheckSerialization(new Changed<GSet<string>>(_keyA, data1));
            CheckSerialization(new DataEnvelope(data1));
            CheckSerialization(new DataEnvelope(data1, ImmutableDictionary.CreateRange(new Dictionary<UniqueAddress, IPruningState>
            {
                { _address1, new PruningPerformed(DateTime.UtcNow) },
                { _address3, new PruningInitialized(_address2, _address1.Address) },
            })));
            CheckSerialization(new Write("A", new DataEnvelope(data1)));
            CheckSerialization(WriteAck.Instance);
            CheckSerialization(new Read("A"));
            CheckSerialization(new ReadResult(new DataEnvelope(data1)));
            CheckSerialization(new ReadResult(null));
            CheckSerialization(new Internal.Status(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, ByteString>("A", ByteString.CopyFromUtf8("a")),
                new KeyValuePair<string, ByteString>("B", ByteString.CopyFromUtf8("b")),
            }), 3, 10));
            CheckSerialization(new Gossip(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, DataEnvelope>("A", new DataEnvelope(data1)),
                new KeyValuePair<string, DataEnvelope>("B", new DataEnvelope(GSet.Create("b").Add("b"))),
            }), true));
        }

        private void CheckSerialization(object expected)
        {
            var serializer = (SerializerWithStringManifest)Sys.Serialization.FindSerializerFor(expected);
            var manifest = serializer.Manifest(expected);
            var blob = serializer.ToBinary(expected);
            var actual = Sys.Serialization.Deserialize(blob, serializer.Identifier, manifest);

            Assert.True(expected.Equals(actual), $"Expected: {expected}\nActual: {actual}");
        }
    }
}
