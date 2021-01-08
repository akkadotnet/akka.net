//-----------------------------------------------------------------------
// <copyright file="ORDictionarySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Akka.Actor;
using Akka.Cluster;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class ORDictionarySpec
    {
        private readonly UniqueAddress _node1;
        private readonly UniqueAddress _node2;

        public ORDictionarySpec(ITestOutputHelper output)
        {
            _node1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2551), 1);
            _node2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2552), 2);
        }

        [Fact]
        public void ORDictionary_must_be_able_to_add_entries()
        {
            var m = ORDictionary.Create(
                (_node1, "a", GSet.Create("A")),
                (_node1, "b", GSet.Create("B")));

            m["a"].Elements.Should().BeEquivalentTo("A");
            m["b"].Elements.Should().BeEquivalentTo("B");

            var m2 = m.SetItem(_node1, "a", GSet.Create("C"));
            m2.Entries["a"].Elements.Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_be_able_to_add_entries_with_delta()
        {
            var m = ORDictionary.Create(
                (_node1, "a", GSet.Create("A")),
                (_node1, "b", GSet.Create("B")));
            var md = m.Delta;

            var m1 = ORDictionary<string, GSet<string>>.Empty.MergeDelta(md);

            m1.Entries["a"].Should().BeEquivalentTo("A");
            m1.Entries["b"].Should().BeEquivalentTo("B");

            var m2 = m1.SetItem(_node1, "a", GSet.Create("C"));
            m2.Entries["a"].Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_be_able_to_remove_entries()
        {
            var m = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node1, "a", GSet.Create("A"))
                .SetItem(_node1, "b", GSet.Create("B"))
                .Remove(_node1, "a");

            m.Entries.Keys.Should().NotContain("a");
            m.Entries.Keys.Should().Contain("b");
        }

        [Fact]
        public void ORDictionary_must_be_able_to_remove_entry_using_a_delta()
        {
            var m = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node1, "a", GSet.Create("A"))
                .SetItem(_node1, "b", GSet.Create("B"));
            var addDelta = m.Delta;
            var removeDelta = m.ResetDelta().RemoveKey(_node1, "a").Delta;

            var m1 = ORDictionary<string, GSet<string>>.Empty.MergeDelta(addDelta);
            m1.Entries.Keys.Should().Contain("a");

            var m2 = m1.MergeDelta(removeDelta);
            m2.Entries.Keys.Should().NotContain("a");
            m2.Entries.Keys.Should().Contain("b");
        }

        [Fact]
        public void ORDictionary_must_be_able_to_add_removed()
        {
            var m = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node1, "a", GSet.Create("A"))
                .SetItem(_node1, "b", GSet.Create("B"))
                .Remove(_node1, "a");

            m.Entries.Keys.Should().NotContain("a");
            m.Entries.Keys.Should().Contain("b");

            var m2 = m.SetItem(_node1, "a", GSet.Create("C"));

            m2.Entries.Keys.Should().Contain("a");
            m2.Entries.Keys.Should().Contain("b");
        }

        [Fact]
        public void ORDictionary_must_be_able_to_have_its_entries_correctly_merged_with_another_ORDictionary_with_other_entries()
        {
            var m1 = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node1, "a", GSet.Create("A"))
                .SetItem(_node1, "b", GSet.Create("B"));

            var m2 = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node2, "c", GSet.Create("C"));

            // merge both ways
            var merged1 = m1.Merge(m2);
            merged1.Entries.Keys.Should().Contain("a");
            merged1.Entries.Keys.Should().Contain("b");
            merged1.Entries.Keys.Should().Contain("c");

            var merged2 = m2.Merge(m1);
            merged2.Entries.Keys.Should().Contain("a");
            merged2.Entries.Keys.Should().Contain("b");
            merged2.Entries.Keys.Should().Contain("c");
        }

        [Fact]
        public void ORDictionary_must_be_able_to_have_its_entries_correctly_merged_with_another_ORDictionary_with_overlaping_entries()
        {
            var m1 = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node1, "a", GSet.Create("A1"))
                .SetItem(_node1, "b", GSet.Create("B1"))
                .Remove(_node1, "a")
                .SetItem(_node1, "d", GSet.Create("D1"));

            var m2 = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node2, "c", GSet.Create("C2"))
                .SetItem(_node2, "a", GSet.Create("A2"))
                .SetItem(_node2, "b", GSet.Create("B2"))
                .Remove(_node2, "b")
                .SetItem(_node2, "d", GSet.Create("D2"));

            // merge both ways
            var merged1 = m1.Merge(m2);
            merged1.Entries.Keys.Should().Contain("a");
            merged1["a"].Elements.Should().BeEquivalentTo("A2");
            merged1.Entries.Keys.Should().Contain("b");
            merged1["b"].Elements.Should().BeEquivalentTo("B1");
            merged1.Entries.Keys.Should().Contain("c");
            merged1.Entries.Keys.Should().Contain("d");
            merged1["d"].Elements.Should().BeEquivalentTo("D1", "D2");

            var merged2 = m2.Merge(m1);
            merged2.Entries.Keys.Should().Contain("a");
            merged2["a"].Elements.Should().BeEquivalentTo("A2");
            merged2.Entries.Keys.Should().Contain("b");
            merged2["b"].Elements.Should().BeEquivalentTo("B1");
            merged2.Entries.Keys.Should().Contain("c");
            merged2.Entries.Keys.Should().Contain("d");
            merged2["d"].Elements.Should().BeEquivalentTo("D1", "D2");
        }

        [Fact]
        public void ORDictionary_must_illustrate_the_danger_of_using_Remove_then_Add_to_replace_an_entry()
        {
            var m1 = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node1, "a", GSet.Create("A"))
                .SetItem(_node1, "b", GSet.Create("B"));

            var m2 = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node2, "c", GSet.Create("C"));

            var merged1 = m1.Merge(m2);
            var m3 = merged1.Remove(_node1, "b").SetItem(_node1, "b", GSet.Create("B2"));
            var merged2 = merged1.Merge(m3);

            merged2["a"].Elements.Should().BeEquivalentTo("A");
            merged2["b"].Elements.Should().BeEquivalentTo("B", "B2");
            merged2["c"].Elements.Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_do_not_have_divergence_in_dot_versions_between_the_underlying_map_and_ORDictionary_delta()
        {
            var m1 = ORDictionary<string, GSet<string>>.Empty.SetItem(_node1, "a", GSet.Create("A"));

            var deltaVersion = default(long?);
            var delta = m1.Delta as ORDictionary<string, GSet<string>>.PutDeltaOperation;
            if (delta != null)
            {
                VersionVector v;
                var addDelta = delta.Underlying as ORSet<string>.AddDeltaOperation;
                if (addDelta != null && addDelta.Underlying.ElementsMap.TryGetValue("a", out v))
                    deltaVersion = v.VersionAt(_node1);
            }

            VersionVector v2;
            var fullVersion = !m1.KeySet.ElementsMap.TryGetValue("a", out v2)
                ? default(long?)
                : v2.VersionAt(_node1);

            deltaVersion.Should().Be(fullVersion);
        }

        [Fact]
        public void ORDictionary_must_not_have_anomalies_for_remove_with_update_scenario_and_deltas()
        {
            var m1 = ORDictionary.Create(_node1, "a", GSet.Create("A")).SetItem(_node1, "b", GSet.Create("B"));
            var m2 = ORDictionary.Create(_node2, "c", GSet.Create("C"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.ResetDelta().Remove(_node1, "b");
            var m4 = merged1.ResetDelta().AddOrUpdate(_node1, "b", GSet<string>.Empty, x => x.Add("B2"));

            var merged2 = m3.Merge(m4);

            merged2.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is included, because GSet("B") is merged with GSet("B2")
            merged2.Entries["b"].Elements.Should().BeEquivalentTo("B", "B2");
            merged2.Entries["c"].Elements.Should().BeEquivalentTo("C");

            var merged3 = m3.MergeDelta(m4.Delta);

            merged3.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is included, because GSet("B") is merged with GSet("B2")
            merged3.Entries["b"].Elements.Should().BeEquivalentTo("B", "B2");
            merged3.Entries["c"].Elements.Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_not_have_anomalies_for_remove_with_update_scenario_and_deltas_2()
        {
            var m1 = ORDictionary.Create(_node1, "a", ORSet.Create(_node1, "A")).SetItem(_node1, "b", ORSet.Create(_node1, "B"));
            var m2 = ORDictionary.Create(_node2, "c", ORSet.Create(_node2, "C"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.ResetDelta().Remove(_node1, "b");
            var m4 = merged1.ResetDelta().Remove(_node1, "b").AddOrUpdate(_node1, "b", ORSet<string>.Empty, x => x.Add(_node1, "B2"));

            var merged2 = m3.Merge(m4);

            merged2.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is not included, because it was removed in both timelines
            merged2.Entries["b"].Elements.Should().BeEquivalentTo("B2");
            merged2.Entries["c"].Elements.Should().BeEquivalentTo("C");

            var merged3 = m3.MergeDelta(m4.Delta);

            merged3.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is not included, because it was removed in both timelines
            merged3.Entries["b"].Elements.Should().BeEquivalentTo("B2");
            merged3.Entries["c"].Elements.Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_not_have_anomalies_for_remove_with_update_scenario_and_deltas_3()
        {
            var m1 = ORDictionary.Create(_node1, "a", ORSet.Create(_node1, "A")).SetItem(_node1, "b", ORSet.Create(_node1, "B"));
            var m2 = ORDictionary.Create(_node2, "c", ORSet.Create(_node2, "C"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.ResetDelta().Remove(_node1, "b");
            var m4 = merged1.ResetDelta().Remove(_node2, "b").AddOrUpdate(_node2, "b", ORSet<string>.Empty, x => x.Add(_node2, "B2"));

            var merged2 = m3.Merge(m4);

            merged2.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is not included, because it was removed in both timelines
            merged2.Entries["b"].Elements.Should().BeEquivalentTo("B2");
            merged2.Entries["c"].Elements.Should().BeEquivalentTo("C");

            var merged3 = m3.MergeDelta(m4.Delta);

            merged3.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is not included, because it was removed in both timelines
            merged3.Entries["b"].Elements.Should().BeEquivalentTo("B2");
            merged3.Entries["c"].Elements.Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_not_have_anomalies_for_remove_with_update_scenario_and_deltas_4()
        {
            var m1 = ORDictionary.Create(_node1, "a", ORSet.Create(_node1, "A")).SetItem(_node1, "b", ORSet.Create(_node1, "B"));
            var m2 = ORDictionary.Create(_node2, "c", ORSet.Create(_node2, "C"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.ResetDelta().Remove(_node1, "b");
            var m4 = merged1.ResetDelta().AddOrUpdate(_node1, "b", ORSet<string>.Empty, x => x.Add(_node1, "B2"));

            var merged2 = m3.Merge(m4);

            merged2.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is included, because ORSet("B") is merged with ORSet("B2")
            merged2.Entries["b"].Elements.Should().BeEquivalentTo("B", "B2");
            merged2.Entries["c"].Elements.Should().BeEquivalentTo("C");

            var merged3 = m3.MergeDelta(m4.Delta);

            merged3.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is included, because ORSet("B") is merged with ORSet("B2")
            merged3.Entries["b"].Elements.Should().BeEquivalentTo("B", "B2");
            merged3.Entries["c"].Elements.Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_not_have_anomalies_for_remove_with_update_scenario_and_deltas_5()
        {
            var m1 = ORDictionary.Create(_node1, "a", GSet.Create("A")).SetItem(_node1, "b", GSet.Create("B"));
            var m2 = ORDictionary.Create(_node2, "c", GSet.Create("C"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.ResetDelta().Remove(_node1, "b");
            var m4 = merged1.ResetDelta().SetItem(_node2, "b", GSet<string>.Empty.Add("B2"));

            var merged2 = m3.Merge(m4);

            merged2.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is not included, because it was removed in both timelines
            merged2.Entries["b"].Elements.Should().BeEquivalentTo("B2");
            merged2.Entries["c"].Elements.Should().BeEquivalentTo("C");

            var merged3 = m3.MergeDelta(m4.Delta);

            merged3.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is not included, because it was removed in both timelines
            merged3.Entries["b"].Elements.Should().BeEquivalentTo("B2");
            merged3.Entries["c"].Elements.Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_not_have_anomalies_for_remove_with_update_scenario_and_deltas_6()
        {
            var m1 = ORDictionary.Create(_node1, "a", ORSet.Create(_node1, "A")).SetItem(_node1, "b", ORSet.Create(_node1, "B"));
            var m2 = ORDictionary.Create(_node2, "b", ORSet.Create(_node2, "B3"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.ResetDelta().Remove(_node1, "b");
            var m4 = merged1.ResetDelta().Remove(_node2, "b")
                .AddOrUpdate(_node2, "b", ORSet<string>.Empty, x => x.Add(_node2, "B1"))
                .AddOrUpdate(_node2, "b", ORSet<string>.Empty, x => x.Add(_node2, "B2"));

            var merged2 = m3.Merge(m4);

            merged2.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is not included, because it was removed in both timelines
            merged2.Entries["b"].Elements.Should().BeEquivalentTo("B1", "B2");

            var merged3 = m3.MergeDelta(m4.Delta);

            merged3.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B is not included, because it was removed in both timelines
            merged3.Entries["b"].Elements.Should().BeEquivalentTo("B1", "B2");
        }

        [Fact]
        public void ORDictionary_must_not_have_anomalies_for_remove_with_update_scenario_and_deltas_7()
        {
            var m1 = ORDictionary.Create(_node1, "a", ORSet.Create(_node1, "A"))
                .SetItem(_node1, "b", ORSet.Create(_node1, "B1"))
                .Remove(_node1, "b");
            var m2 = ORDictionary.Create(_node1, "a", ORSet.Create(_node1, "A"))
                .SetItem(_node1, "b", ORSet.Create(_node1, "B2"));
            var m2d = m2.ResetDelta().Remove(_node1, "b");
            var m2u = m2.ResetDelta()
                .AddOrUpdate(_node1, "b", ORSet<string>.Empty, x => x.Add(_node1, "B3"))
                .AddOrUpdate(_node2, "b", ORSet<string>.Empty, x => x.Add(_node2, "B4"));

            var merged1 = m1.Merge(m2d).MergeDelta(m2u.Delta);

            merged1.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // note that B1 is lost as it was added and removed earlier in timeline than B2
            merged1.Entries["b"].Elements.Should().BeEquivalentTo("B2", "B3", "B4");
        }

        [Fact]
        public void ORDictionary_must_not_have_anomalies_for_remove_with_update_scenario_and_deltas_8()
        {
            var m1 = ORDictionary.Create(_node1, "a", GSet.Create("A"))
                .SetItem(_node1, "b", GSet.Create("B"))
                .SetItem(_node2, "b", GSet.Create("B"));
            var m2 = ORDictionary.Create(_node2, "c", GSet.Create("C"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.ResetDelta().Remove(_node1, "b").Remove(_node2, "b");
            var m4 = merged1.ResetDelta()
                .SetItem(_node2, "b", GSet.Create("B2"))
                .SetItem(_node2, "b", GSet.Create("B3"));

            var merged2 = m3.Merge(m4);

            merged2.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged2.Entries["b"].Elements.Should().BeEquivalentTo("B3");
            merged2.Entries["c"].Elements.Should().BeEquivalentTo("C");

            var merged3 = merged1.MergeDelta(m3.Delta).MergeDelta(m4.Delta);

            merged3.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged3.Entries["b"].Elements.Should().BeEquivalentTo("B3");
            merged3.Entries["c"].Elements.Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_not_have_anomalies_for_remove_with_update_scenario_and_deltas_9()
        {
            var m1 = ORDictionary.Create(_node1, "a", GSet.Create("A"))
                .SetItem(_node1, "b", GSet.Create("B"))
                .SetItem(_node2, "b", GSet.Create("B"));
            var m2 = ORDictionary.Create(_node2, "c", GSet.Create("C"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.ResetDelta().Remove(_node1, "b").Remove(_node2, "b");
            var m4 = merged1.ResetDelta()
                .AddOrUpdate(_node2, "b", GSet<string>.Empty, x => x.Add("B2"))
                .AddOrUpdate(_node2, "b", GSet<string>.Empty, x => x.Add("B3"));

            var merged2 = m3.Merge(m4);

            merged2.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged2.Entries["b"].Elements.Should().BeEquivalentTo("B2", "B3");
            merged2.Entries["c"].Elements.Should().BeEquivalentTo("C");

            var merged3 = merged1.MergeDelta(m3.Delta).MergeDelta(m4.Delta);

            merged3.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged3.Entries["b"].Elements.Should().BeEquivalentTo("B2", "B3");
            merged3.Entries["c"].Elements.Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_not_have_anomalies_for_remove_with_update_scenario_and_deltas_10()
        {
            var m1 = ORDictionary.Create(_node1, "a", GSet.Create("A"))
                .SetItem(_node2, "b", GSet.Create("B"));
            var m3 = m1.ResetDelta().Remove(_node2, "b");
            var m4 = m3.ResetDelta()
                .SetItem(_node2, "b", GSet.Create("B2"))
                .AddOrUpdate(_node2, "b", GSet<string>.Empty, x => x.Add("B3"));

            var merged2 = m3.Merge(m4);

            merged2.Entries["a"].Should().BeEquivalentTo("A");
            merged2.Entries["b"].Should().BeEquivalentTo("B2", "B3");

            var merged3 = m3.MergeDelta(m4.Delta);

            merged3.Entries["a"].Should().BeEquivalentTo("A");
            merged3.Entries["b"].Should().BeEquivalentTo("B2", "B3");
        }

        [Fact]
        public void ORDictionary_must_not_have_anomalies_for_remove_with_update_scenario_and_deltas_11()
        {
            var m1 = ORDictionary.Create(_node1, "a", GSet.Create("A"));
            var m2 = ORDictionary.Create(_node2, "a", GSet<string>.Empty).Remove(_node2, "a");

            var merged1 = m1.Merge(m2);
            merged1.Entries["a"].Elements.Should().BeEquivalentTo("A");

            var merged2 = m1.MergeDelta(m2.Delta);
            merged2.Entries["a"].Elements.Should().BeEquivalentTo("A");
        }

        [Fact]
        public void ORDictionary_must_have_usual_anomalies_for_remove_with_update_scenario()
        {
            // please note that the current ORMultiMap has the same anomaly
            // because the condition of keeping global vvector is violated
            // by removal of the whole entry for the removed key "b" which results in removal of it's value's vvector
            var m1 = ORDictionary.Create(_node1, "a", ORSet.Create(_node1, "A")).SetItem(_node1, "b", ORSet.Create(_node1, "B"));
            var m2 = ORDictionary.Create(_node2, "c", ORSet.Create(_node2, "C"));

            // m1 - node1 gets the update from m2
            var merged1 = m1.Merge(m2);
            // m2 - node2 gets the update from m1
            var merged2 = m2.Merge(m1);

            // RACE CONDITION ahead!
            var m3 = merged1.ResetDelta().Remove(_node1, "b");
            // let's imagine that m3 (node1) update gets propagated here (full state or delta - doesn't matter)
            // and is in flight, but in the meantime, an element is being added somewhere else (m4 - node2)
            // and the update is propagated before the update from node1 is merged
            var m4 = merged2.ResetDelta().AddOrUpdate(_node2, "b", ORSet<string>.Empty, x => x.Add(_node2, "B2"));
            // and later merged on node1
            var merged3 = m3.Merge(m4);
            // and the other way round...
            var merged4 = m4.Merge(m3);

            // result -  the element "B" is kept on both sides...
            merged3.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged3.Entries["b"].Elements.Should().BeEquivalentTo("B", "B2");
            merged3.Entries["c"].Elements.Should().BeEquivalentTo("C");

            merged4.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged4.Entries["b"].Elements.Should().BeEquivalentTo("B", "B2");
            merged4.Entries["c"].Elements.Should().BeEquivalentTo("C");

            // but if the timing was slightly different, so that the update from node1
            // would get merged just before update on node2:
            var merged5 = m2.Merge(m3)
                .ResetDelta()
                .AddOrUpdate(_node2, "b", ORSet<string>.Empty, x => x.Add(_node2, "B2"));
            // the update propagated ... and merged on node1:
            var merged6 = m3.Merge(merged5);

            // then the outcome is different... because the vvector of value("b") was lost...
            merged5.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // this time it's different...
            merged5.Entries["b"].Elements.Should().BeEquivalentTo("B2");
            merged5.Entries["c"].Elements.Should().BeEquivalentTo("C");

            merged6.Entries["a"].Elements.Should().BeEquivalentTo("A");
            // this time it's different...
            merged6.Entries["b"].Elements.Should().BeEquivalentTo("B2");
            merged6.Entries["c"].Elements.Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_work_with_delta_coalescing_scenario_1()
        {
            var m1 = ORDictionary.Create(_node1, "a", GSet.Create("A")).SetItem(_node1, "b", GSet.Create("B"));
            var m2 = m1.ResetDelta()
                .SetItem(_node2, "b", GSet.Create("B2"))
                .AddOrUpdate(_node2, "b", GSet<string>.Empty, x => x.Add("B3"));

            var merged1 = m1.Merge(m2);

            merged1.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged1.Entries["b"].Elements.Should().BeEquivalentTo("B", "B2", "B3");

            var merged2 = m1.MergeDelta(m2.Delta);

            merged2.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged2.Entries["b"].Elements.Should().BeEquivalentTo("B", "B2", "B3");

            var m3 = ORDictionary.Create(_node1, "a", GSet.Create("A")).SetItem(_node2, "b", GSet.Create("B"));
            var m4 = m3.ResetDelta().SetItem(_node2, "b", GSet.Create("B2")).SetItem(_node2, "b", GSet.Create("B3"));

            var merged3 = m3.Merge(m4);

            merged3.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged3.Entries["b"].Elements.Should().BeEquivalentTo("B", "B3");

            var merged4 = m3.MergeDelta(m4.Delta);

            merged4.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged4.Entries["b"].Elements.Should().BeEquivalentTo("B", "B3");

            var m5 = ORDictionary.Create(_node1, "a", GSet.Create("A")).SetItem(_node2, "b", GSet.Create("B"));
            var m6 = m5.ResetDelta()
                .SetItem(_node2, "b", GSet.Create("B2"))
                .AddOrUpdate(_node2, "b", GSet<string>.Empty, x => x.Add("B3"))
                .AddOrUpdate(_node2, "b", GSet<string>.Empty, x => x.Add("B4"));

            var merged5 = m5.Merge(m6);

            merged5.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged5.Entries["b"].Elements.Should().BeEquivalentTo("B", "B2", "B3", "B4");

            var merged6 = m5.MergeDelta(m6.Delta);

            merged6.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged6.Entries["b"].Elements.Should().BeEquivalentTo("B", "B2", "B3", "B4");

            var m7 = ORDictionary.Create(_node1, "a", GSet.Create("A")).SetItem(_node2, "b", GSet.Create("B"));
            var m8 = m7.ResetDelta()
                .SetItem(_node2, "b", GSet.Create("B2"))
                .SetItem(_node2, "d", GSet.Create("D"))
                .SetItem(_node2, "b", GSet.Create("B3"));

            var merged7 = m7.Merge(m8);

            merged7.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged7.Entries["b"].Elements.Should().BeEquivalentTo("B", "B3");
            merged7.Entries["d"].Elements.Should().BeEquivalentTo("D");

            var merged8 = m7.MergeDelta(m8.Delta);

            merged8.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged8.Entries["b"].Elements.Should().BeEquivalentTo("B", "B3");
            merged8.Entries["d"].Elements.Should().BeEquivalentTo("D");

            var m9 = ORDictionary.Create(_node1, "a", GSet.Create("A")).SetItem(_node2, "b", GSet.Create("B"));
            var m10 = m9.ResetDelta()
                .SetItem(_node2, "b", GSet.Create("B2"))
                .SetItem(_node2, "d", GSet.Create("D"))
                .Remove(_node2, "d")
                .SetItem(_node2, "b", GSet.Create("B3"));

            var merged9 = m9.Merge(m10);

            merged9.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged9.Entries["b"].Elements.Should().BeEquivalentTo("B", "B3");

            var merged10 = m9.MergeDelta(m10.Delta);

            merged10.Entries["a"].Elements.Should().BeEquivalentTo("A");
            merged10.Entries["b"].Elements.Should().BeEquivalentTo("B", "B3");
        }

        [Fact]
        public void ORDictionary_must_work_with_deltas_and_Add_for_GSet_elements_type()
        {
            var m1 = ORDictionary.Create(_node1, "a", GSet.Create("A"));
            var m2 = m1.ResetDelta().AddOrUpdate(_node1, "a", GSet<string>.Empty, x => x.Add("B"));
            var m3 = ORDictionary<string, GSet<string>>.Empty.MergeDelta(m1.Delta).MergeDelta(m2.Delta);
            m3.Entries["a"].Should().BeEquivalentTo("A", "B");
        }

        [Fact]
        public void ORDictionary_must_not_allow_SetItem_for_ORSet_elements_type()
        {
            var m = ORDictionary<string, ORSet<string>>.Empty
                .SetItem(_node1, "a", ORSet.Create(_node1, "A"));

            Assert.Throws<ArgumentException>(() =>
                m.SetItem(_node1, "a", ORSet.Create(_node1, "B")));
        }

        [Fact]
        public void ORDictionary_must_work_with_aggregated_deltas_and_Add_for_GSet_elements_type()
        {
            var m1 = ORDictionary.Create(_node1, "a", GSet.Create("A"));
            var m2 = m1.ResetDelta()
                .AddOrUpdate(_node1, "a", GSet<string>.Empty, x => x.Add("B"))
                .AddOrUpdate(_node1, "a", GSet<string>.Empty, x => x.Add("C"));
            var m3 = ORDictionary<string, GSet<string>>.Empty.MergeDelta(m1.Delta).MergeDelta(m2.Delta);
            m3.Entries["a"].Should().BeEquivalentTo("A", "B", "C");
        }

        [Fact]
        public void ORDictionary_must_work_with_deltas_and_Increment_for_GCounter_elements_type()
        {
            var m1 = ORDictionary.Create(_node1, "a", GCounter.Empty);
            var m2 = m1.ResetDelta().AddOrUpdate(_node1, "a", GCounter.Empty, x => x.Increment(_node1, 10));
            var m3 = m2.ResetDelta().AddOrUpdate(_node2, "a", GCounter.Empty, x => x.Increment(_node2, 10));
            var m4 = ORDictionary<string, GCounter>.Empty
                .MergeDelta(m1.Delta)
                .MergeDelta(m2.Delta)
                .MergeDelta(m3.Delta);
            m4.Entries["a"].Value.Should().Be(20UL);
        }

        [Fact]
        public void ORDictionary_must_work_with_deltas_and_Increment_for_PNCounter_elements_type()
        {
            var m1 = ORDictionary.Create(_node1, "a", PNCounter.Empty);
            var m2 = m1.ResetDelta().AddOrUpdate(_node1, "a", PNCounter.Empty, x => x.Increment(_node1, 10));
            var m3 = m2.ResetDelta().AddOrUpdate(_node2, "a", PNCounter.Empty, x => x.Decrement(_node2, 10));
            var m4 = ORDictionary<string, PNCounter>.Empty
                .MergeDelta(m1.Delta)
                .MergeDelta(m2.Delta)
                .MergeDelta(m3.Delta);
            m4.Entries["a"].Value.Should().Be(new BigInteger(0));
        }

        [Fact]
        public void ORDictionary_must_work_with_deltas_and_updates_for_Flag_elements_type()
        {
            var m1 = ORDictionary.Create(_node1, "a", Flag.False);
            var m2 = m1.ResetDelta().AddOrUpdate(_node1, "a", Flag.False, x => x.SwitchOn());
            var m3 = ORDictionary<string, Flag>.Empty
                .MergeDelta(m1.Delta)
                .MergeDelta(m2.Delta);
            m3.Entries["a"].Enabled.Should().BeTrue();
        }

        [Fact]
        public void ORDictionary_must_be_able_to_update_an_entry()
        {
            var m1 = ORDictionary<string, ORSet<string>>.Empty
                .SetItem(_node1, "a", ORSet.Create(_node1, "A"))
                .SetItem(_node1, "b", ORSet.Create(_node1, "B01").Add(_node1, "B02").Add(_node1, "B03"));

            var m2 = ORDictionary<string, ORSet<string>>.Empty
                .SetItem(_node2, "c", ORSet.Create(_node2, "C"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.AddOrUpdate(_node1, "b", ORSet<string>.Empty, old => old.Clear(_node1).Add(_node1, "B2"));

            var merged2 = merged1.Merge(m3);
            merged2["a"].Elements.Should().BeEquivalentTo("A");
            merged2["b"].Elements.Should().BeEquivalentTo("B2");
            merged2["c"].Elements.Should().BeEquivalentTo("C");

            var m4 = merged1.AddOrUpdate(_node2, "b", ORSet<string>.Empty, old => old.Add(_node2, "B3"));
            var merged3 = m3.Merge(m4);
            merged3["a"].Elements.Should().BeEquivalentTo("A");
            merged3["b"].Elements.Should().BeEquivalentTo("B2", "B3");
            merged3["c"].Elements.Should().BeEquivalentTo("C");
        }

        [Fact]
        public void ORDictionary_must_be_able_to_update_ORSet_entry_with_Remove_then_Add()
        {
            var m1 = ORDictionary<string, ORSet<string>>.Empty
                .SetItem(_node1, "a", ORSet.Create(_node1, "A01"))
                .AddOrUpdate(_node1, "a", ORSet<string>.Empty, old => old.Add(_node1, "A02"))
                .AddOrUpdate(_node1, "a", ORSet<string>.Empty, old => old.Add(_node1, "A03"))
                .SetItem(_node1, "b", ORSet.Create(_node1, "B01").Add(_node1, "B02").Add(_node1, "B03"));

            var m2 = ORDictionary<string, ORSet<string>>.Empty
                .SetItem(_node2, "c", ORSet.Create(_node2, "C"));

            var merged1 = m1.Merge(m2);

            // note that remove + put work because the new VersionVector version is incremented
            // from a global counter

            var m3 = merged1.Remove(_node1, "b").SetItem(_node1, "b", ORSet.Create(_node1, "B2"));

            var merged2 = merged1.Merge(m3);
            Assert.Equal(ImmutableHashSet.Create("A01", "A02", "A03"), merged2["a"].Elements);
            Assert.Equal(ImmutableHashSet.Create("B2"), merged2["b"].Elements);
            Assert.Equal(ImmutableHashSet.Create("C"), merged2["c"].Elements);

            var m4 = merged1.AddOrUpdate(_node2, "b", ORSet<string>.Empty, old => old.Add(_node2, "B3"));
            var merged3 = m3.Merge(m4);
            Assert.Equal(ImmutableHashSet.Create("A01", "A02", "A03"), merged3["a"].Elements);
            Assert.Equal(ImmutableHashSet.Create("B2", "B3"), merged3["b"].Elements);
            Assert.Equal(ImmutableHashSet.Create("C"), merged3["c"].Elements);
        }

        [Fact]
        public void ORDictionary_must_be_able_to_update_ORSet_entry_with_Remove_then_Merge_then_Add()
        {
            var m1 = ORDictionary<string, ORSet<string>>.Empty
                .SetItem(_node1, "a", ORSet.Create(_node1, "A"))
                .SetItem(_node1, "b", ORSet.Create(_node1, "B01").Add(_node1, "B02").Add(_node1, "B03"));

            var m2 = ORDictionary<string, ORSet<string>>.Empty
                .SetItem(_node2, "c", ORSet.Create(_node2, "C"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.Remove(_node1, "b");

            var merged2 = merged1.Merge(m3);
            Assert.Equal(ImmutableHashSet.Create("A"), merged2["a"].Elements);
            Assert.DoesNotContain("b", merged2.Entries.Keys);
            Assert.Equal(ImmutableHashSet.Create("C"), merged2["c"].Elements);

            var m4 = merged2.SetItem(_node1, "b", ORSet.Create(_node1, "B2"));
            var m5 = merged2.AddOrUpdate(_node2, "c", ORSet<string>.Empty, old => old.Add(_node2, "C2"))
                .SetItem(_node2, "b", ORSet.Create(_node2, "B3"));

            var merged3 = m5.Merge(m4);
            Assert.Equal(ImmutableHashSet.Create("A"), merged3["a"].Elements);
            Assert.Equal(ImmutableHashSet.Create("B2", "B3"), merged3["b"].Elements);
            Assert.Equal(ImmutableHashSet.Create("C", "C2"), merged3["c"].Elements);
        }
    }
}
