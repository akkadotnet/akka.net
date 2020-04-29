//-----------------------------------------------------------------------
// <copyright file="ORMultiDictionarySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Akka.DistributedData.Internal;
using Akka.DistributedData.Serialization;
using Akka.DistributedData;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class ORMultiDictionarySpec
    {
        private readonly UniqueAddress _node1;
        private readonly UniqueAddress _node2;

        public ORMultiDictionarySpec(ITestOutputHelper output)
        {
            _node1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2551), 1);
            _node2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2552), 2);
        }

        [Fact]
        public void ORMultiDictionary_must_be_able_to_add_entries()
        {
            var m = ORMultiValueDictionary<string, string>.Empty.AddItem(_node1, "a", "A").AddItem(_node1, "b", "B");
            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create("A")),
                new KeyValuePair<string, IImmutableSet<string>>("b", ImmutableHashSet.Create("B"))
            }), m.Entries);

            var m2 = m.AddItem(_node1, "a", "C");
            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create("A", "C")),
                new KeyValuePair<string, IImmutableSet<string>>("b", ImmutableHashSet.Create("B"))
            }), m2.Entries);
        }

        [Fact]
        public void ORMultiDictionary_must_be_able_to_remove_entries()
        {
            var m = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A")
                .AddItem(_node1, "b", "B")
                .RemoveItem(_node1, "a", "A");

            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, IImmutableSet<string>>("b", ImmutableHashSet.Create("B"))
            }), m.Entries);
        }

        [Fact]
        public void ORMultiDictionary_must_be_able_to_replace_entries()
        {
            var m = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A")
                .ReplaceItem(_node1, "a", "A", "B");

            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create("B"))
            }), m.Entries);
        }

        [Fact]
        public void ORMultiDictionary_must_be_able_to_have_its_entries_correctly_merged_with_another_ORMultiDictionary_with_other_entries()
        {
            var m1 = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A")
                .AddItem(_node1, "b", "B");

            var m2 = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node2, "c", "C");

            // merge both ways
            var expected = ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create("A")),
                new KeyValuePair<string, IImmutableSet<string>>("b", ImmutableHashSet.Create("B")),
                new KeyValuePair<string, IImmutableSet<string>>("c", ImmutableHashSet.Create("C"))
            });

            var merged1 = m1.Merge(m2);
            Assert.Equal(expected, merged1.Entries);

            var merged2 = m2.Merge(m1);
            Assert.Equal(expected, merged2.Entries);
        }

        [Fact]
        public void ORMultiDictionary_must_be_able_to_have_its_entries_correctly_merged_with_another_ORMultiDictionary_with_overlaping_entries()
        {
            var m1 = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A1")
                .AddItem(_node1, "b", "B1")
                .RemoveItem(_node1, "a", "A1")
                .AddItem(_node1, "d", "D1");

            var m2 = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node2, "c", "C2")
                .AddItem(_node2, "a", "A2")
                .AddItem(_node2, "b", "B2")
                .RemoveItem(_node2, "b", "B2")
                .AddItem(_node2, "d", "D2");
            
            var merged1 = m1.Merge(m2);
            merged1.Entries["a"].Should().BeEquivalentTo("A2");
            merged1.Entries["b"].Should().BeEquivalentTo("B1");
            merged1.Entries["c"].Should().BeEquivalentTo("C2");
            merged1.Entries["d"].Should().BeEquivalentTo("D1", "D2");

            var merged2 = m2.Merge(m1);
            merged2.Entries["a"].Should().BeEquivalentTo("A2");
            merged2.Entries["b"].Should().BeEquivalentTo("B1");
            merged2.Entries["c"].Should().BeEquivalentTo("C2");
            merged2.Entries["d"].Should().BeEquivalentTo("D1", "D2");
        }

        [Fact]
        public void ORMultiDictionary_must_be_able_to_have_its_entries_correctly_merged_with_another_ORMultiDictionary_with_overlaping_entries_2()
        {
            var m1 = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node1, "b", "B1");

            var m2 = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node2, "b", "B2")
                .Remove(_node2, "b");
            
            var merged1 = m1.Merge(m2);
            merged1.Entries["b"].Should().BeEquivalentTo("B1");

            var merged2 = m2.Merge(m1);
            merged2.Entries["b"].Should().BeEquivalentTo("B1");

            var merged3 = m1.MergeDelta(m2.Delta);
            merged3.Entries["b"].Should().BeEquivalentTo("B1");

            var merged4 = m2.MergeDelta(m1.Delta);
            merged4.Entries["b"].Should().BeEquivalentTo("B1");
        }

        [Fact]
        public void ORMultiDictionary_must_not_have_anomalies_for_remove_then_update_scenario_and_deltas()
        {
            var m2a = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node1, "q", "Q")
                .RemoveItem(_node1, "q", "Q");
            var m1 = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node1, "z", "Z")
                .AddItem(_node2, "x", "X")
                .RemoveItem(_node1, "z", "Z");
            var m2 = m2a.ResetDelta().RemoveItem(_node2, "a", "A");

            var merged1 = m1.Merge(m2);

            merged1.ContainsKey("a").Should().BeFalse();

            var merged2 = m1.MergeDelta(m2.Delta);

            merged2.ContainsKey("a").Should().BeFalse();
        }

        [Fact]
        public void ORMultiDictionary_must_be_able_to_get_all_bindings_for_an_entry_and_then_reduce_them_upon_putting_them_back()
        {
            var m = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A1")
                .AddItem(_node1, "a", "A2")
                .AddItem(_node1, "b", "B1");

            IImmutableSet<string> a;
            m.TryGetValue("a", out a);
            Assert.Equal(ImmutableHashSet.Create("A1", "A2"), a);

            var m2 = m.SetItems(_node1, "a", a.Remove("A1"));
            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create("A2")),
                new KeyValuePair<string, IImmutableSet<string>>("b", ImmutableHashSet.Create("B1"))
            }), m2.Entries);
        }

        [Fact]
        public void ORMultiDictionary_must_return_the_value_for_an_existing_key_and_the_default_for_a_non_existing_one()
        {
            var m = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A");

            IImmutableSet<string> v;
            m.TryGetValue("a", out v).Should().BeTrue();
            v.Should().BeEquivalentTo("A");

            m.TryGetValue("b", out v).Should().BeFalse();
        }

        [Fact]
        public void ORMultiDictionary_must_remove_all_bindings_for_a_given_key()
        {
            var m = ORMultiValueDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A1")
                .AddItem(_node1, "a", "A2")
                .AddItem(_node1, "b", "B1");

            var m2 = m.Remove(_node1, "a");
            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, IImmutableSet<string>>("b", ImmutableHashSet.Create("B1"))
            }), m2.Entries);
        }

        [Fact]
        public void ORMultiDictionary_must_not_have_anomalies_for_Remove_then_AddItem_scenario_and_delta_deltas()
        {
            var m1 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));

            var m2 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node2, "c", ImmutableHashSet.Create("C"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.ResetDelta().Remove(_node1, "b");
            var m4 = m3.ResetDelta().AddItem(_node1, "b", "B2");

            var merged2 = m3.Merge(m4);

            merged2.Entries["a"].Should().BeEquivalentTo("A");
            merged2.Entries["b"].Should().BeEquivalentTo("B2");
            merged2.Entries["c"].Should().BeEquivalentTo("C");

            var merged3 = m3.MergeDelta(m4.Delta);

            merged3.Entries["a"].Should().BeEquivalentTo("A");
            merged3.Entries["b"].Should().BeEquivalentTo("B2");
            merged3.Entries["c"].Should().BeEquivalentTo("C");

            var merged4 = merged1.Merge(m3).Merge(m4);

            merged4.Entries["a"].Should().BeEquivalentTo("A");
            merged4.Entries["b"].Should().BeEquivalentTo("B2");
            merged4.Entries["c"].Should().BeEquivalentTo("C");

            var merged5 = merged1.MergeDelta(m3.Delta).MergeDelta(m4.Delta);

            merged5.Entries["a"].Should().BeEquivalentTo("A");
            merged5.Entries["b"].Should().BeEquivalentTo("B2");
            merged5.Entries["c"].Should().BeEquivalentTo("C");

            var merged6 = merged1.MergeDelta((ORDictionary<string, ORSet<string>>.IDeltaOperation)m3.Delta.Merge(m4.Delta));

            merged6.Entries["a"].Should().BeEquivalentTo("A");
            merged6.Entries["b"].Should().BeEquivalentTo("B2");
            merged6.Entries["c"].Should().BeEquivalentTo("C");
        }

        /// <summary>
        /// Bug reproduction: https://github.com/akkadotnet/akka.net/issues/4302
        /// </summary>
        [Fact]
        public void Bugfix_4302_ORMultiValueDictionary_Deltas_must_merge_other_ORMultiValueDictionary()
        {
            var m1 = ORMultiValueDictionary<string, string>.Empty
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B1"));

            var m2 = ORMultiValueDictionary<string, string>.Empty
                .SetItems(_node2, "c", ImmutableHashSet.Create("C"))
                .SetItems(_node2, "b", ImmutableHashSet.Create("B2"));

            // This is how deltas really get merged inside the replicator
            var dataEnvelope = new DataEnvelope(m1.Delta);
            if (dataEnvelope.Data is IReplicatedDelta withDelta)
            {
                dataEnvelope = dataEnvelope.WithData(withDelta.Zero.MergeDelta(withDelta));
            }

            // Bug: this is was an ORDictionary<string, ORSet<string>> under #4302
            var storedData = dataEnvelope.Data;

            // simulate merging an update
            var merged1 = (ORMultiValueDictionary<string, string>)m2.Merge(storedData);

            merged1.Entries["a"].Should().BeEquivalentTo("A");
            merged1.Entries["b"].Should().BeEquivalentTo("B1", "B2");
            merged1.Entries["c"].Should().BeEquivalentTo("C");
        }

        /// <summary>
        /// Bug reproduction: https://github.com/akkadotnet/akka.net/issues/4367
        /// </summary>
        [Fact]
        public void Bugfix_4367_ORMultiValueDictionary_Deltas_must_merge_other_ORMultiValueDictionary()
        {
            var m1 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B1"));

            var m2 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node2, "c", ImmutableHashSet.Create("C"))
                .SetItems(_node2, "b", ImmutableHashSet.Create("B2"));

            // This is how deltas really get merged inside the replicator
            var dataEnvelope = new DataEnvelope(m1.Delta);
            if (dataEnvelope.Data is IReplicatedDelta withDelta)
            {
                dataEnvelope = dataEnvelope.WithData(withDelta.Zero.MergeDelta(withDelta));
            }

            // Bug: this is was an ORDictionary<string, ORSet<string>> under #4302
            var storedData = dataEnvelope.Data;

            // simulate merging an update
            var merged1 = (ORMultiValueDictionary<string, string>)m2.Merge(storedData);

            merged1.Entries["a"].Should().BeEquivalentTo("A");
            merged1.Entries["b"].Should().BeEquivalentTo("B1", "B2");
            merged1.Entries["c"].Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORMultiDictionary_must_not_have_anomalies_for_Remove_then_AddItem_scenario_and_delta_deltas_2()
        {
            // the new delta-delta ORMultiMap is free from this anomaly
            var m1 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));

            var m2 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node2, "c", ImmutableHashSet.Create("C"));

            // m1 - node1 gets the update from m2
            var merged1 = m1.Merge(m2);
            // m2 - node2 gets the update from m1
            var merged2 = m2.Merge(m1);

            // no race condition
            var m3 = merged1.ResetDelta().Remove(_node1, "b");
            // let's imagine that m3 (node1) update gets propagated here (full state or delta - doesn't matter)
            // and is in flight, but in the meantime, an element is being added somewhere else (m4 - node2)
            // and the update is propagated before the update from node1 is merged
            var m4 = merged2.ResetDelta().AddItem(_node2, "b", "B2");
            // and later merged on node1
            var merged3 = m3.Merge(m4);
            // and the other way round...
            var merged4 = m4.Merge(m3);

            // result -  the element "B" is kept on both sides...
            merged3.Entries["a"].Should().BeEquivalentTo("A");
            merged3.Entries["b"].Should().BeEquivalentTo("B2");
            merged3.Entries["c"].Should().BeEquivalentTo("C");

            merged4.Entries["a"].Should().BeEquivalentTo("A");
            merged4.Entries["b"].Should().BeEquivalentTo("B2");
            merged4.Entries["c"].Should().BeEquivalentTo("C");

            // but if the timing was slightly different, so that the update from node1
            // would get merged just before update on node2:
            var merged5 = m2.Merge(m3).ResetDelta().AddItem(_node2, "b", "B2");
            // the update propagated ... and merged on node1:
            var merged6 = m3.Merge(merged5);

            // then the outcome would be the same...
            merged5.Entries["a"].Should().BeEquivalentTo("A");
            merged5.Entries["b"].Should().BeEquivalentTo("B2");
            merged5.Entries["c"].Should().BeEquivalentTo("C");

            merged6.Entries["a"].Should().BeEquivalentTo("A");
            merged6.Entries["b"].Should().BeEquivalentTo("B2");
            merged6.Entries["c"].Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORMultiDictionary_must_work_with_delta_coalescing_scenario_1()
        {
            var m1 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));
            var m2 = m1.ResetDelta()
                .SetItems(_node2, "b", ImmutableHashSet.Create("B2"))
                .AddItem(_node2, "b", "B3");

            var merged1 = m1.Merge(m2);

            merged1.Entries["a"].Should().BeEquivalentTo("A");
            merged1.Entries["b"].Should().BeEquivalentTo("B2", "B3");

            var merged2 = m1.MergeDelta(m2.Delta);

            merged2.Entries["a"].Should().BeEquivalentTo("A");
            merged2.Entries["b"].Should().BeEquivalentTo("B2", "B3");

            var m3 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));
            var m4 = m3.ResetDelta()
                .SetItems(_node2, "b", ImmutableHashSet.Create("B2"))
                .SetItems(_node2, "b", ImmutableHashSet.Create("B3"));

            var merged3 = m3.Merge(m4);

            merged3.Entries["a"].Should().BeEquivalentTo("A");
            merged3.Entries["b"].Should().BeEquivalentTo("B3");

            var merged4 = m3.MergeDelta(m4.Delta);

            merged4.Entries["a"].Should().BeEquivalentTo("A");
            merged4.Entries["b"].Should().BeEquivalentTo("B3");

            var m5 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));
            var m6 = m5.ResetDelta()
                .SetItems(_node2, "b", ImmutableHashSet.Create("B2"))
                .AddItem(_node2, "b", "B3")
                .AddItem(_node2, "b", "B4");

            var merged5 = m5.Merge(m6);

            merged5.Entries["a"].Should().BeEquivalentTo("A");
            merged5.Entries["b"].Should().BeEquivalentTo("B2", "B3", "B4");

            var merged6 = m5.MergeDelta(m6.Delta);

            merged6.Entries["a"].Should().BeEquivalentTo("A");
            merged6.Entries["b"].Should().BeEquivalentTo("B2", "B3", "B4");

            var m7 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));
            var m8 = m7.ResetDelta()
                .SetItems(_node2, "d", ImmutableHashSet.Create("D"))
                .AddItem(_node2, "b", "B3")
                .SetItems(_node2, "b", ImmutableHashSet.Create("B4"));

            var merged7 = m7.Merge(m8);

            merged7.Entries["a"].Should().BeEquivalentTo("A");
            merged7.Entries["b"].Should().BeEquivalentTo("B4");
            merged7.Entries["d"].Should().BeEquivalentTo("D");

            var merged8 = m7.MergeDelta(m8.Delta);

            merged8.Entries["a"].Should().BeEquivalentTo("A");
            merged8.Entries["b"].Should().BeEquivalentTo("B4");
            merged8.Entries["d"].Should().BeEquivalentTo("D");

            var m9 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));
            var m10 = m9.ResetDelta()
                .AddItem(_node2, "b", "B3")
                .AddItem(_node2, "b", "B4");

            var merged9 = m9.Merge(m10);

            merged9.Entries["a"].Should().BeEquivalentTo("A");
            merged9.Entries["b"].Should().BeEquivalentTo("B", "B3", "B4");

            var merged10 = m9.MergeDelta(m10.Delta);

            merged10.Entries["a"].Should().BeEquivalentTo("A");
            merged10.Entries["b"].Should().BeEquivalentTo("B", "B3", "B4");

            var m11 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B1"))
                .Remove(_node1, "b");
            var m12 = m11.ResetDelta()
                .AddItem(_node2, "b", "B2")
                .AddItem(_node2, "b", "B3");

            var merged11 = m11.Merge(m12);

            merged11.Entries["a"].Should().BeEquivalentTo("A");
            merged11.Entries["b"].Should().BeEquivalentTo("B2", "B3");

            var merged12 = m11.MergeDelta(m12.Delta);

            merged12.Entries["a"].Should().BeEquivalentTo("A");
            merged12.Entries["b"].Should().BeEquivalentTo("B2", "B3");

            var m13 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B1"))
                .Remove(_node1, "b");
            var m14 = m13.ResetDelta()
                .AddItem(_node2, "b", "B2")
                .SetItems(_node2, "b", ImmutableHashSet.Create("B3"));

            var merged13 = m13.Merge(m14);

            merged13.Entries["a"].Should().BeEquivalentTo("A");
            merged13.Entries["b"].Should().BeEquivalentTo("B3");

            var merged14 = m13.MergeDelta(m14.Delta);

            merged14.Entries["a"].Should().BeEquivalentTo("A");
            merged14.Entries["b"].Should().BeEquivalentTo("B3");

            var m15 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B", "B1"))
                .SetItems(_node1, "c", ImmutableHashSet.Create("C"));
            var m16 = m15.ResetDelta()
                .AddItem(_node2, "b", "B2")
                .AddItem(_node2, "c", "C1");

            var merged15 = m15.Merge(m16);

            merged15.Entries["a"].Should().BeEquivalentTo("A");
            merged15.Entries["b"].Should().BeEquivalentTo("B", "B1", "B2");
            merged15.Entries["c"].Should().BeEquivalentTo("C", "C1");

            var merged16 = m15.MergeDelta(m16.Delta);

            merged16.Entries["a"].Should().BeEquivalentTo("A");
            merged16.Entries["b"].Should().BeEquivalentTo("B", "B1", "B2");
            merged16.Entries["c"].Should().BeEquivalentTo("C", "C1");

            // somewhat artificial setup
            var m17 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B", "B1"));
            var m18 = m17.ResetDelta().AddItem(_node2, "b", "B2");
            var m19 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                .ResetDelta()
                .SetItems(_node2, "b", ImmutableHashSet.Create("B3"));

            var merged17 = m17.Merge(m18).Merge(m19);

            merged17.Entries["a"].Should().BeEquivalentTo("A");
            merged17.Entries["b"].Should().BeEquivalentTo("B", "B1", "B3");

            var merged18 = m17.MergeDelta((ORDictionary<string, ORSet<string>>.IDeltaOperation)m18.Delta.Merge(m19.Delta));

            merged18.Entries["a"].Should().BeEquivalentTo("A");
            merged18.Entries["b"].Should().BeEquivalentTo("B", "B1", "B3");
        }

        [Fact]
        public void ORMultiDictionary_must_work_with_delta_coalescing_scenario_2()
        {
            var m1 = ORMultiValueDictionary<string, string>.Empty
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));
            var m2 = m1.ResetDelta()
                .SetItems(_node2, "b", ImmutableHashSet.Create("B2"))
                .AddItem(_node2, "b", "B3");

            var merged1 = m1.Merge(m2);

            merged1.Entries["a"].Should().BeEquivalentTo("A");
            merged1.Entries["b"].Should().BeEquivalentTo("B2", "B3");

            var merged2 = m2.MergeDelta(m2.Delta);

            merged2.Entries["a"].Should().BeEquivalentTo("A");
            merged2.Entries["b"].Should().BeEquivalentTo("B2", "B3");

            var m3 = ORMultiValueDictionary<string, string>.Empty
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));
            var m4 = m3.ResetDelta()
                .SetItems(_node2, "b", ImmutableHashSet.Create("B2"))
                .SetItems(_node2, "b", ImmutableHashSet.Create("B3"));

            var merged3 = m3.Merge(m4);

            merged3.Entries["a"].Should().BeEquivalentTo("A");
            merged3.Entries["b"].Should().BeEquivalentTo("B3");

            var merged4 = m3.MergeDelta(m4.Delta);

            merged4.Entries["a"].Should().BeEquivalentTo("A");
            merged4.Entries["b"].Should().BeEquivalentTo("B3");

            var m5 = ORMultiValueDictionary<string, string>.Empty
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));
            var m6 = m5.ResetDelta()
                .SetItems(_node2, "b", ImmutableHashSet.Create("B2"))
                .AddItem(_node2, "b", "B3")
                .AddItem(_node2, "b", "B4");

            var merged5 = m5.Merge(m6);

            merged5.Entries["a"].Should().BeEquivalentTo("A");
            merged5.Entries["b"].Should().BeEquivalentTo("B2", "B3", "B4");

            var merged6 = m5.MergeDelta(m6.Delta);

            merged6.Entries["a"].Should().BeEquivalentTo("A");
            merged6.Entries["b"].Should().BeEquivalentTo("B2", "B3", "B4");

            var m7 = ORMultiValueDictionary<string, string>.Empty
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));
            var m8 = m7.ResetDelta()
                .SetItems(_node2, "d", ImmutableHashSet.Create("D"))
                .AddItem(_node2, "b", "B3")
                .SetItems(_node2, "b", ImmutableHashSet.Create("B4"));

            var merged7 = m7.Merge(m8);

            merged7.Entries["a"].Should().BeEquivalentTo("A");
            merged7.Entries["b"].Should().BeEquivalentTo("B4");
            merged7.Entries["d"].Should().BeEquivalentTo("D");

            var merged8 = m7.MergeDelta(m8.Delta);

            merged8.Entries["a"].Should().BeEquivalentTo("A");
            merged8.Entries["b"].Should().BeEquivalentTo("B4");
            merged8.Entries["d"].Should().BeEquivalentTo("D");

            var m9 = ORMultiValueDictionary<string, string>.Empty
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B"));
            var m10 = m9.ResetDelta()
                .AddItem(_node2, "b", "B3")
                .AddItem(_node2, "b", "B4");

            var merged9 = m9.Merge(m10);

            merged9.Entries["a"].Should().BeEquivalentTo("A");
            merged9.Entries["b"].Should().BeEquivalentTo("B", "B3", "B4");

            var merged10 = m9.MergeDelta(m10.Delta);

            merged10.Entries["a"].Should().BeEquivalentTo("A");
            merged10.Entries["b"].Should().BeEquivalentTo("B", "B3", "B4");

            var m11 = ORMultiValueDictionary<string, string>.Empty
                .SetItems(_node1, "a", ImmutableHashSet.Create("A"))
                .SetItems(_node1, "b", ImmutableHashSet.Create("B1"))
                .Remove(_node1, "b");
            var m12 = m11.ResetDelta()
                .AddItem(_node2, "b", "B2")
                .AddItem(_node2, "b", "B3");

            var merged11 = m11.Merge(m12);

            merged11.Entries["a"].Should().BeEquivalentTo("A");
            merged11.Entries["b"].Should().BeEquivalentTo("B2", "B3");

            var merged12 = m11.MergeDelta(m12.Delta);

            merged12.Entries["a"].Should().BeEquivalentTo("A");
            merged12.Entries["b"].Should().BeEquivalentTo("B2", "B3");
        }

        [Fact]
        public void ORMultiDictionary_must_work_with_tombstones_for_ORMutliValueDictionary_WithValueDeltas_and_its_delta_delta_operations()
        {
            {
                // ORMultiMap.withValueDeltas has the following (public) interface:
                // put - place (or replace) a value in a destructive way - no tombstone is created
                //       this can be seen in the relevant delta: PutDeltaOp(AddDeltaOp(ORSet(a)),(a,ORSet()),ORMultiMapWithValueDeltasTag)
                // remove - to avoid anomalies that ORMultiMap has, value for the key being removed is being cleared
                //          before key removal, this can be seen in the following deltas created by the remove op (depending on situation):
                //          DeltaGroup(Vector(PutDeltaOp(AddDeltaOp(ORSet(a)),(a,ORSet()),ORMultiMapWithValueDeltasTag), RemoveKeyDeltaOp(RemoveDeltaOp(ORSet(a)),a,ORMultiMapWithValueDeltasTag)))
                //          DeltaGroup(Vector(UpdateDeltaOp(AddDeltaOp(ORSet(c)),Map(c -> FullStateDeltaOp(ORSet())),ORMultiMapWithValueDeltasTag), RemoveKeyDeltaOp(RemoveDeltaOp(ORSet(c)),c,ORMultiMapWithValueDeltasTag)))
                //          after applying the remove operation the tombstone for the given map looks as follows: Map(a -> ORSet()) (or Map(c -> ORSet()) )
                var m1 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                    .SetItems(_node1, "a", ImmutableHashSet.Create("A"));
                var m2 = m1.ResetDelta().Remove(_node1, "a");

                var m3 = m1.MergeDelta(m2.Delta);
                var m4 = m1.Merge(m2);

                m3.Underlying.ValueMap["a"].Elements.Should()
                    .BeEmpty(); // tombstone for 'a' - but we can probably optimize that away, read on
                m4.Underlying.ValueMap["a"].Elements.Should()
                    .BeEmpty(); // tombstone for 'a' - but we can probably optimize that away, read on

                var m5 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                    .SetItems(_node1, "a", ImmutableHashSet.Create("A1"));
                m3.MergeDelta(m5.Delta).Entries["a"].Should().BeEquivalentTo("A1");
                m4.MergeDelta(m5.Delta).Entries["a"].Should().BeEquivalentTo("A1");
                m4.Merge(m5).Entries["a"].Should().BeEquivalentTo("A1");
            }

            {
                // addBinding - add a binding for a certain value - no tombstone is created
                //              this operation works through "updated" call of the underlying ORMap, that is not exposed
                //              in the ORMultiMap interface
                //              the side-effect of addBinding is that it can lead to anomalies with the standard "ORMultiMap"

                // removeBinding - remove binding for a certain value, and if there are no more remaining elements, remove
                //                 the now superfluous key, please note that for .withValueDeltas variant tombstone will be created

                var um1 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas.AddItem(_node1, "a", "A");
                var um2 = um1.ResetDelta().RemoveItem(_node1, "a", "A");

                var um3 = um1.MergeDelta(um2.Delta);
                var um4 = um1.Merge(um2);

                um3.Underlying.ValueMap["a"].Should()
                    .BeEmpty(); // tombstone for 'a' - but we can probably optimize that away, read on
                um4.Underlying.ValueMap["a"].Should()
                    .BeEmpty(); // tombstone for 'a' - but we can probably optimize that away, read on

                var um5 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas.AddItem(_node1, "a", "A1");
                um3.MergeDelta(um5.Delta).Entries["a"].Should().BeEquivalentTo("A1");
                um4.MergeDelta(um5.Delta).Entries["a"].Should().BeEquivalentTo("A1");
                um4.Merge(um5).Entries["a"].Should().BeEquivalentTo("A1");
            }

            {
                // replaceBinding - that would first addBinding for new binding and then removeBinding for old binding
                //                  so no tombstone would be created

                // so the only option to create a tombstone with non-zero (!= Set() ) contents would be to call removeKey (not remove!)
                // for the underlying ORMap (or have a removeKeyOp delta that does exactly that)
                // but this is not possible in applications, as both remove and removeKey operations are API of internal ORMap
                // and are not externally exposed in the ORMultiMap, and deltas are causal, so removeKeyOp delta cannot arise
                // without previous delta containing 'clear' or 'put' operation setting the tombstone at Set()
                // the example shown below cannot happen in practice

                var tm1 = new ORMultiValueDictionary<string, string>(
                    ORMultiValueDictionary<string, string>.EmptyWithValueDeltas.AddItem(_node1, "a", "A").Underlying
                        .RemoveKey(_node1, "a"), true);
                tm1.Underlying.ValueMap["a"].Elements.Should().BeEquivalentTo("A"); // tombstone
                tm1.AddItem(_node1, "a", "A1").Entries["a"].Should().BeEquivalentTo("A", "A1");

                var tm2 = ORMultiValueDictionary<string, string>.EmptyWithValueDeltas
                    .AddItem(_node1, "a", "A")
                    .ResetDelta()
                    .AddItem(_node1, "a", "A1");
                tm1.MergeDelta(tm2.Delta).Entries["a"].Should().BeEquivalentTo("A", "A1");
                tm1.Merge(tm2).Entries["a"].Should().BeEquivalentTo("A", "A1");

                var tm3 = new ORMultiValueDictionary<string, string>(
                    ORMultiValueDictionary<string, string>.EmptyWithValueDeltas.AddItem(_node1, "a", "A").Underlying
                        .Remove(_node1, "a"), true);
                tm3.Underlying.ContainsKey("a").Should().BeFalse(); // no tombstone, because remove not removeKey
                tm3.MergeDelta(tm2.Delta).Entries.Should()
                    .BeEmpty(); // no tombstone - update delta could not be applied
                tm3.Merge(tm2).Entries.Should().BeEmpty();

                // This situation gives us possibility of removing the impact of tombstones altogether, as the only valid value for tombstone
                // created by means of either API call or application of delta propagation would be Set()
                // then the tombstones being only empty sets can be entirely cleared up
                // because the merge delta operation will use in that case the natural zero from the delta.
                // Thus in case of valid API usage and normal operation of delta propagation no tombstones will be created.
            }
        }
    }
}
