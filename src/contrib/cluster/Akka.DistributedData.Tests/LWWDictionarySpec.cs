//-----------------------------------------------------------------------
// <copyright file="LWWDictionarySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Akka.DistributedData.Internal;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class LWWDictionarySpec
    {
        private readonly UniqueAddress _node1;
        private readonly UniqueAddress _node2;

        public LWWDictionarySpec(ITestOutputHelper output)
        {
            _node1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2551), 1);
            _node2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2552), 2);
        }

        [Fact]
        public void LWWDictionary_must_be_able_to_set_entries()
        {
            var m = LWWDictionary.Create(
                (_node1, "a", 1),
                (_node2, "b", 2));

            Assert.Equal(m.Entries, ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string,int>("a", 1),
                new KeyValuePair<string,int>("b", 2)
            }));
        }

        [Fact]
        public void LWWDictionary_must_be_able_to_have_its_entries_correctly_merged_with_another_LWWMap_with_other_entries()
        {
            var m1 = LWWDictionary.Create(
                (_node1, "a", 1),
                (_node1, "b", 2));
            var m2 = LWWDictionary.Create(_node2, "c", 3);

            var expected = ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, int>("a", 1),
                new KeyValuePair<string, int>("b", 2),
                new KeyValuePair<string, int>("c", 3),
            });

            // merge both ways
            Assert.Equal(expected, m1.Merge(m2).Entries);
            Assert.Equal(expected, m2.Merge(m1).Entries);
        }

        [Fact]
        public void LWWDictionary_must_be_able_to_remove_entry()
        {
            var m1 = LWWDictionary.Create(
                (_node1, "a", 1),
                (_node2, "b", 2));
            var m2 = LWWDictionary.Create(_node2, "c", 3);

            var merged1 = m1.Merge(m2);

            var m3 = merged1.Remove(_node1, "b");
            Assert.Equal(merged1.Merge(m3).Entries, ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, int>("a", 1),
                new KeyValuePair<string, int>("c", 3)
            }));

            // but if there is a conflicting update the entry is not removed
            var m4 = merged1.SetItem(_node2, "b", 22);
            Assert.Equal(m3.Merge(m4).Entries, ImmutableDictionary.CreateRange(new Dictionary<string, int>
            {
                {"a", 1},
                {"b", 22},
                {"c", 3}
            }));
        }

        [Fact]
        public void LWWDictionary_must_be_able_to_work_with_deltas()
        {
            var m1 = LWWDictionary<string, int>.Empty.SetItem(_node1, "a", 1).SetItem(_node1, "b", 2);
            var m2 = LWWDictionary<string, int>.Empty.SetItem(_node2, "c", 3);

            var expected = ImmutableDictionary.CreateRange(new Dictionary<string, int>
            {
                {"a", 1},
                {"b", 2},
                {"c", 3}
            });
            m1.Merge(m2).Entries.Should().BeEquivalentTo(expected);
            m2.Merge(m1).Entries.Should().BeEquivalentTo(expected);

            LWWDictionary<string, int>.Empty.MergeDelta(m1.Delta).MergeDelta(m2.Delta).Entries.Should().BeEquivalentTo(expected);
            LWWDictionary<string, int>.Empty.MergeDelta(m2.Delta).MergeDelta(m1.Delta).Entries.Should().BeEquivalentTo(expected);

            var merged1 = m1.Merge(m2);

            var m3 = merged1.ResetDelta().Remove(_node1, "b");
            merged1.MergeDelta(m3.Delta).Entries.Should().BeEquivalentTo(new Dictionary<string, int>
            {
                {"a", 1},
                {"c", 3}
            });

            // but if there is a conflicting update the entry is not removed
            var m4 = merged1.ResetDelta().SetItem(_node2, "b", 22);
            m3.MergeDelta(m4.Delta).Entries.Should().BeEquivalentTo(new Dictionary<string, int>
            {
                {"a", 1},
                {"b", 22},
                {"c", 3}
            });
        }

        /// <summary>
        /// Bug reproduction: https://github.com/akkadotnet/akka.net/issues/4400
        /// </summary>
        [Fact]
        public async Task Bugfix_4400_LWWDictionary_Deltas_must_merge_other_LWWDictionary()
        {
            var m1 = LWWDictionary<string, string>.Empty
                .SetItem(_node1, "a", "A")
                .SetItem(_node1, "b", "B1");

            await Task.Delay(200);

            var m2 = LWWDictionary<string, string>.Empty
                .SetItem(_node2, "c", "C")
                .SetItem(_node2, "b", "B2");

            // This is how deltas really get merged inside the replicator
            var dataEnvelope = new DataEnvelope(m1.Delta);
            if (dataEnvelope.Data is IReplicatedDelta withDelta)
            {
                dataEnvelope = dataEnvelope.WithData(withDelta.Zero.MergeDelta(withDelta));
            }

            // Bug: this is was an ORDictionary<string, ORSet<string>> under #4302
            var storedData = dataEnvelope.Data;

            // simulate merging an update
            var merged1 = (LWWDictionary<string, string>)m2.Merge(storedData);

            merged1.Entries["a"].Should().BeEquivalentTo("A");
            merged1.Entries["b"].Should().BeEquivalentTo("B2");
            merged1.Entries["c"].Should().BeEquivalentTo("C");
        }
    }
}
