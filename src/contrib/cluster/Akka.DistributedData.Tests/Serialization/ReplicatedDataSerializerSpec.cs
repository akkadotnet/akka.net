//-----------------------------------------------------------------------
// <copyright file="ReplicatedDataSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Linq;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using Akka.Configuration;
using Akka.DistributedData.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests.Serialization
{
    [Collection("DistributedDataSpec")]
    public class ReplicatedDataSerializerSpec : TestKit.Xunit2.TestKit
    {
        private static readonly Config BaseConfig = ConfigurationFactory.ParseString(@"
            akka.actor {
                provider=""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                serializers {
                  wire = ""Akka.Serialization.WireSerializer, Akka.Serialization.Wire""
                }
                serialization-bindings {
                  ""Akka.DistributedData.IReplicatedDataSerialization, Akka.DistributedData"" = wire
                }
            }
            akka.remote.helios.tcp.port = 0").WithFallback(DistributedData.DefaultConfig());
        
        private readonly UniqueAddress _address1 = new UniqueAddress(new Address("akka.tcp", "sys", "some.host.org", 4711), 1);
        private readonly UniqueAddress _address2 = new UniqueAddress(new Address("akka.tcp", "sys", "other.host.org", 4711), 2);
        private readonly UniqueAddress _address3 = new UniqueAddress(new Address("akka.tcp", "sys", "some.host.org", 4711), 3);

        public ReplicatedDataSerializerSpec(ITestOutputHelper output) : base(BaseConfig, "ReplicatedDataSerializerSpec", output: output)
        {
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_GSet()
        {
            CheckSerialization(GSet<string>.Empty);
            CheckSerialization(GSet.Create("a"));
            CheckSerialization(GSet.Create("a", "b"));
            CheckSerialization(GSet.Create(1, 2, 3));
            CheckSerialization(GSet.Create(_address1, _address2));
            CheckSerialization(GSet.Create<object>(1L, "2", 3, _address1));

            CheckSameContent(GSet.Create("a", "b"), GSet.Create("a", "b"));
            CheckSameContent(GSet.Create("a", "b"), GSet.Create("b", "a"));
            CheckSameContent(GSet.Create(_address1, _address2, _address3), GSet.Create(_address2, _address1, _address3));
            CheckSameContent(GSet.Create(_address1, _address2, _address3), GSet.Create(_address3, _address2, _address1));
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_ORSet()
        {
            CheckSerialization(ORSet<string>.Empty);
            CheckSerialization(ORSet.Create(_address1, "a"));
            CheckSerialization(ORSet.Create(_address1, "a").Add(_address2, "a"));
            CheckSerialization(ORSet.Create(_address1, "a").Remove(_address2, "a"));
            CheckSerialization(ORSet.Create(_address1, "a").Add(_address2, "b").Remove(_address1, "a"));
            CheckSerialization(ORSet.Create(_address1, 1).Add(_address2, 2));
            CheckSerialization(ORSet.Create(_address1, 1L).Add(_address2, 2L));
            CheckSerialization(ORSet.Create<object>(_address1, "a").Add(_address2, 2).Add(_address3, 3L).Add(_address3, _address3));

            var s1 = ORSet.Create(_address1, "a").Add(_address2, "b");
            var s2 = ORSet.Create(_address2, "b").Add(_address1, "a");

            CheckSameContent(s1.Merge(s2), s2.Merge(s1));

            var s3 = ORSet.Create<object>(_address1, "a").Add(_address2, 17).Remove(_address3, 17);
            var s4 = ORSet.Create<object>(_address2, 17).Remove(_address3, 17).Add(_address1, "a");

            CheckSameContent(s3.Merge(s4), s4.Merge(s3));
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_Flag()
        {
            CheckSerialization(Flag.False);
            CheckSerialization(Flag.False.SwitchOn());
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_LWWRegister()
        {
            CheckSerialization(new LWWRegister<string>(_address1, "value1"));
            CheckSerialization(new LWWRegister<string>(_address2, "value2").WithValue(_address2, "value3"));
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_GCounter()
        {
            CheckSerialization(GCounter.Empty);
            CheckSerialization(GCounter.Empty.Increment(_address1, 3));
            CheckSerialization(GCounter.Empty.Increment(_address1, 2).Increment(_address2, 5));

            CheckSameContent(
                GCounter.Empty.Increment(_address1, 2).Increment(_address2, 5),
                GCounter.Empty.Increment(_address2, 5).Increment(_address1, 1).Increment(_address1, 1));
            CheckSameContent(
                GCounter.Empty.Increment(_address1, 2).Increment(_address3, 5),
                GCounter.Empty.Increment(_address3, 5).Increment(_address1, 2));
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_PNCounter()
        {
            CheckSerialization(PNCounter.Empty);
            CheckSerialization(PNCounter.Empty.Increment(_address1, 3));
            CheckSerialization(PNCounter.Empty.Increment(_address1, 3).Decrement(_address1, 1));
            CheckSerialization(PNCounter.Empty.Increment(_address1, 2).Increment(_address2, 5));
            CheckSerialization(PNCounter.Empty.Increment(_address1, 2).Increment(_address2, 5).Decrement(_address1, 1));

            CheckSameContent(
                PNCounter.Empty.Increment(_address1, 2).Increment(_address2, 5),
                PNCounter.Empty.Increment(_address2, 5).Increment(_address1, 1).Increment(_address1, 1));
            CheckSameContent(
                PNCounter.Empty.Increment(_address1, 2).Increment(_address3, 5),
                PNCounter.Empty.Increment(_address3, 5).Increment(_address1, 2));
            CheckSameContent(
                PNCounter.Empty.Increment(_address1, 2).Decrement(_address1, 1).Increment(_address3, 5),
                PNCounter.Empty.Increment(_address3, 5).Increment(_address1, 2).Decrement(_address1, 1));
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_ORDictionary()
        {
            CheckSerialization(ORDictionary<string, GSet<string>>.Empty);
            CheckSerialization(ORDictionary<string, GSet<string>>.Empty.SetItem(_address1, "a", GSet.Create("A")));
            CheckSerialization(ORDictionary<string, GSet<string>>.Empty.SetItem(_address1, "a", GSet.Create("A")).SetItem(_address2, "b", GSet.Create("B")));
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_LWWDictionary()
        {
            CheckSerialization(LWWDictionary<string, string>.Empty);
            CheckSerialization(LWWDictionary<string, string>.Empty.SetItem(_address1, "a", "value1"));
            CheckSerialization(LWWDictionary<string, object>.Empty.SetItem(_address1, "a", "value1").SetItem(_address2, "b", 17));
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_PNCounterDictionary()
        {
            CheckSerialization(PNCounterDictionary<string>.Empty);
            CheckSerialization(PNCounterDictionary<string>.Empty.Increment(_address1, "a", 3));
            CheckSerialization(PNCounterDictionary<string>.Empty
                .Increment(_address1, "a", 3)
                .Decrement(_address2, "a", 2)
                .Increment(_address2, "b", 5));
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_ORMultiDictionary()
        {
            CheckSerialization(ORMultiDictionary<string, string>.Empty);
            CheckSerialization(ORMultiDictionary<string, string>.Empty.AddItem(_address1, "a", "A"));
            CheckSerialization(ORMultiDictionary<string, string>.Empty
                .AddItem(_address1, "a", "A1")
                .SetItems(_address2, "b", ImmutableHashSet.CreateRange(new[] { "B1", "B2", "B3" }))
                .AddItem(_address2, "a", "A2"));

            var m1 = ORMultiDictionary<string, string>.Empty.AddItem(_address1, "a", "A1").AddItem(_address2, "a", "A2");
            var m2 = ORMultiDictionary<string, string>.Empty.SetItems(_address2, "b", ImmutableHashSet.CreateRange(new[] { "B1", "B2", "B3" }));
            CheckSameContent(m1.Merge(m2), m2.Merge(m1));
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_DeletedData()
        {
            CheckSerialization(DeletedData.Instance);
        }

        [Fact]
        public void ReplicatedDataSerializer_should_serialize_VersionVector()
        {
            CheckSerialization(VersionVector.Empty);
            CheckSerialization(VersionVector.Create(_address1, 1));
            CheckSerialization(VersionVector.Empty.Increment(_address1).Increment(_address2));

            var v1 = VersionVector.Empty.Increment(_address1).Increment(_address1);
            var v2 = VersionVector.Empty.Increment(_address2);
            CheckSameContent(v1.Merge(v2), v2.Merge(v1));
        }

        private void CheckSerialization<T>(T expected)
        {
            var serializer = Sys.Serialization.FindSerializerFor(expected);
            var blob = serializer.ToBinary(expected);
            var actual = serializer.FromBinary(blob, expected.GetType());
            
            // we cannot use Assert.Equal here since ORMultiDictionary will be resolved as
            // IEnumerable<KeyValuePair<string, ImmutableHashSet<string>> and immutable sets
            // fails on structural equality
            Assert.True(expected.Equals(actual), $"Expected: {expected}\nActual: {actual}");
        }

        private void CheckSameContent(object a, object b)
        {
            // we cannot use Assert.Equal here since ORMultiDictionary will be resolved as
            // IEnumerable<KeyValuePair<string, ImmutableHashSet<string>> and immutable sets
            // fails on structural equality
            Assert.True(a.Equals(b));
            var serializer = Sys.Serialization.FindSerializerFor(a);
            var blobA = serializer.ToBinary(a);
            var blobB = serializer.ToBinary(b);
            Assert.Equal(blobA.Length, blobB.Length);
            for (int i = 0; i < blobA.Length; i++)
                Assert.Equal(blobA[i], blobB[i]);
        }
    }
}