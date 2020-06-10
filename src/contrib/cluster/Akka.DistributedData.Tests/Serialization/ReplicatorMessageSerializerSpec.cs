//-----------------------------------------------------------------------
// <copyright file="ReplicatorMessageSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Akka.DistributedData.Internal;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using Address = Akka.Actor.Address;
using UniqueAddress = Akka.Cluster.UniqueAddress;
using Akka.DistributedData.Serialization;
using Akka.DistributedData.Durable;
using System.Linq;

namespace Akka.DistributedData.Tests.Serialization
{
    [Collection("DistributedDataSpec")]
    public class ReplicatorMessageSerializerSpec : TestKit.Xunit2.TestKit
    {
        private static readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
            akka.actor {
                provider=""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            }
            akka.remote.dot-netty.tcp.port = 0").WithFallback(DistributedData.DefaultConfig());

        private readonly ReplicatorMessageSerializer _serializer;

        private readonly string _protocol;

        private readonly UniqueAddress _address1;
        private readonly UniqueAddress _address2;
        private readonly UniqueAddress _address3;

        private readonly GSetKey<string> _keyA;

        public ReplicatorMessageSerializerSpec(ITestOutputHelper output) : base(BaseConfig, "ReplicatorMessageSerializerSpec", output)
        {
            _serializer = new ReplicatorMessageSerializer((ExtendedActorSystem)Sys);

            // We dont have Artery implementation
            // _protocol = ((RemoteActorRefProvider) ((ExtendedActorSystem)Sys).Provider).RemoteSettings.Artery.Enabled 
            _protocol = "akka.tcp";

            _address1 = new UniqueAddress(new Address("akka.tcp", Sys.Name, "some.host.org", 4711), 1);
            _address2 = new UniqueAddress(new Address("akka.tcp", Sys.Name, "other.host.org", 4711), 2);
            _address3 = new UniqueAddress(new Address("akka.tcp", Sys.Name, "some.host.org", 4711), 3);

            _keyA = new GSetKey<string>("A");
        }

        [Fact()]
        public void ReplicatorMessageSerializer_should_serialize_Replicator_message()
        {
            var ref1 = Sys.ActorOf(Props.Empty, "ref1");
            var data1 = GSet.Create("a");
            var delta1 = GCounter.Empty.Increment(_address1, 17).Increment(_address2, 2).Delta;
            var delta2 = delta1.Increment(_address2, 1).Delta;
            var delta3 = ORSet<string>.Empty.Add(_address1, "a").Delta;
            var delta4 = ORMultiValueDictionary<string, string>.Empty.AddItem(_address1, "a", "b").Delta;

            CheckSerialization(new Get(_keyA, ReadLocal.Instance));
            CheckSerialization(new Get(_keyA, new ReadMajority(TimeSpan.FromSeconds(2)), "x"));
            CheckSerialization(new Get(_keyA, new ReadMajority(TimeSpan.FromMilliseconds(((double)int.MaxValue) + 50)), "x"));
            CheckSerialization(new Get(_keyA, new ReadMajority(TimeSpan.FromSeconds(2), 3), "x"));
            _serializer.Invoking(s =>
            {
                s.ToBinary(new Get(_keyA, new ReadMajority(TimeSpan.FromMilliseconds(((double)int.MaxValue) * 3)), "x"));
            })
                .ShouldThrow<ArgumentOutOfRangeException>("Our protobuf protocol does not support timeouts larger than unsigned ints")
                .Which.Message.Contains("unsigned int");

            CheckSerialization(new GetSuccess(_keyA, null, data1));
            CheckSerialization(new GetSuccess(_keyA, "x", data1));
            CheckSerialization(new NotFound(_keyA, "x"));
            CheckSerialization(new GetFailure(_keyA, "x"));
            CheckSerialization(new Subscribe(_keyA, ref1));
            CheckSerialization(new Unsubscribe(_keyA, ref1));
            CheckSerialization(new Changed(_keyA, data1));
            CheckSerialization(new DataEnvelope(data1));
            CheckSerialization(new DataEnvelope(data1, ImmutableDictionary.CreateRange(new Dictionary<UniqueAddress, IPruningState>
            {
                { _address1, new PruningPerformed(DateTime.UtcNow) },
                { _address3, new PruningInitialized(_address2, _address1.Address) },
            })));
            CheckSerialization(new Write("A", new DataEnvelope(data1), _address1));
            CheckSerialization(WriteAck.Instance);
            CheckSerialization(WriteNack.Instance);
            CheckSerialization(DeltaNack.Instance);
            CheckSerialization(new Read("A", _address1));
            CheckSerialization(new ReadResult(new DataEnvelope(data1)));
            CheckSerialization(new ReadResult(null));

            CheckSerialization(new Internal.Status(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, ByteString>("A", ByteString.CopyFromUtf8("a")),
                new KeyValuePair<string, ByteString>("B", ByteString.CopyFromUtf8("b")),
            }), 3, 10, 17, 19));

            CheckSerialization(new Internal.Status(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, ByteString>("A", ByteString.CopyFromUtf8("a")),
                new KeyValuePair<string, ByteString>("B", ByteString.CopyFromUtf8("b")),
            }), 3, 10, null, 19)); // (from scala code) can be None when sending back to a node of version 2.5.21

            CheckSerialization(new Gossip(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, DataEnvelope>("A", new DataEnvelope(data1)),
                new KeyValuePair<string, DataEnvelope>("B", new DataEnvelope(GSet.Create("b").Add("c"))),
            }), true, 17, 19));

            CheckSerialization(new Gossip(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, DataEnvelope>("A", new DataEnvelope(data1)),
                new KeyValuePair<string, DataEnvelope>("B", new DataEnvelope(GSet.Create("b").Add("c"))),
            }), true, null, 19)); // (from scala code) can be None when sending back to a node of version 2.5.21

            CheckSerialization(new DeltaPropagation(_address1, true,
                ImmutableDictionary.CreateRange(new Dictionary<string, Delta>
                {
                    { "A", new Delta(new DataEnvelope(delta1), 1, 1) },
                    { "B", new Delta(new DataEnvelope(delta2), 3, 5) },
                    { "C", new Delta(new DataEnvelope(delta3), 1, 1) },
                    { "DC", new Delta(new DataEnvelope(delta4), 1, 1) }
                })));

            CheckSerialization(new DurableDataEnvelope(data1));

            var pruning = ImmutableDictionary.CreateRange(new Dictionary<UniqueAddress, IPruningState>
                {
                    { _address1, new PruningPerformed(DateTime.UtcNow) },
                    { _address3, new PruningInitialized(_address2, _address1.Address) },
                });
            var deserializedDurableDataEnvelope = CheckSerialization(
                new DurableDataEnvelope(new DataEnvelope(data1, pruning, new SingleVersionVector(_address1, 13))));
            var expectedPruning = pruning
                .Where(kvp => kvp.Value is PruningPerformed)
                .ToDictionary(k => k.Key, v => v.Value);
            deserializedDurableDataEnvelope.DataEnvelope.Pruning.ShouldAllBeEquivalentTo(expectedPruning);
            deserializedDurableDataEnvelope.DataEnvelope.DeltaVersions.Count.Should().Be(0);
        }

        private T CheckSerialization<T>(T expected)
        {
            var blob = _serializer.ToBinary(expected);
            var manifest = _serializer.Manifest(expected);
            var actual = _serializer.FromBinary(blob, manifest);

            //actual.ShouldBeEquivalentTo(expected);
            actual.Should().Be(expected);
            actual.Should().BeOfType(typeof(T));
            return (T)actual;
        }
    }
}
