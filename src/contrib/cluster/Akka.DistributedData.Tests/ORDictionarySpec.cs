//-----------------------------------------------------------------------
// <copyright file="ORDictionarySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
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
        public void A_ORDictionary_should_be_able_to_add_entries()
        {
            var m = ORDictionary.Create(
                Tuple.Create(_node1, "a", GSet.Create("A")),
                Tuple.Create(_node1, "b", GSet.Create("B")));

            Assert.Equal(ImmutableHashSet.Create("A"), m["a"].Elements);
            Assert.Equal(ImmutableHashSet.Create("B"), m["b"].Elements);

            var m2 = m.SetItem(_node1, "a", GSet.Create("C"));
            Assert.Equal(ImmutableHashSet.Create("C"), m2.Entries["a"].Elements);
        }

        [Fact]
        public void A_ORDictionary_should_be_able_to_remove_entries()
        {
            var m = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node1, "a", GSet.Create("A"))
                .SetItem(_node1, "b", GSet.Create("B"))
                .Remove(_node1, "a");

            Assert.DoesNotContain("a", m.Entries.Keys);
            Assert.Contains("b", m.Entries.Keys);
        }

        [Fact]
        public void A_ORDictionary_should_be_able_to_add_removed()
        {
            var m = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node1, "a", GSet.Create("A"))
                .SetItem(_node1, "b", GSet.Create("B"))
                .Remove(_node1, "a");

            Assert.DoesNotContain("a", m.Entries.Keys);
            Assert.Contains("b", m.Entries.Keys);

            var m2 = m.SetItem(_node1, "a", GSet.Create("C"));

            Assert.Contains("a", m2.Entries.Keys);
            Assert.Contains("b", m2.Entries.Keys);
        }

        [Fact]
        public void A_ORDictionary_should_be_able_to_have_its_entries_correctly_merged_with_another_ORDictionary_with_other_entries()
        {
            var m1 = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node1, "a", GSet.Create("A"))
                .SetItem(_node1, "b", GSet.Create("B"));

            var m2 = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node2, "c", GSet.Create("C"));

            // merge both ways
            var merged1 = m1.Merge(m2);
            Assert.Contains("a", merged1.Entries.Keys);
            Assert.Contains("b", merged1.Entries.Keys);
            Assert.Contains("c", merged1.Entries.Keys);

            var merged2 = m2.Merge(m1);
            Assert.Contains("a", merged2.Entries.Keys);
            Assert.Contains("b", merged2.Entries.Keys);
            Assert.Contains("c", merged2.Entries.Keys);
        }

        [Fact]
        public void A_ORDictionary_should_be_able_to_have_its_entries_correctly_merged_with_another_ORDictionary_with_overlaping_entries()
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
            Assert.Contains("a", merged1.Entries.Keys);
            Assert.Equal(ImmutableHashSet.Create("A2"), merged1["a"].Elements);
            Assert.Contains("b", merged1.Entries.Keys);
            Assert.Equal(ImmutableHashSet.Create("B1"), merged1["b"].Elements);
            Assert.Contains("c", merged1.Entries.Keys);
            Assert.Contains("d", merged1.Entries.Keys);
            Assert.Equal(ImmutableHashSet.Create("D1", "D2"), merged1["d"].Elements);

            var merged2 = m2.Merge(m1);
            Assert.Contains("a", merged2.Entries.Keys);
            Assert.Equal(ImmutableHashSet.Create("A2"), merged2["a"].Elements);
            Assert.Contains("b", merged2.Entries.Keys);
            Assert.Equal(ImmutableHashSet.Create("B1"), merged2["b"].Elements);
            Assert.Contains("c", merged2.Entries.Keys);
            Assert.Contains("d", merged2.Entries.Keys);
            Assert.Equal(ImmutableHashSet.Create("D1", "D2"), merged2["d"].Elements);
        }

        [Fact]
        public void A_ORDictionary_should_illustrate_the_danger_of_using_Remove_then_Add_to_replace_an_entry()
        {
            var m1 = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node1, "a", GSet.Create("A"))
                .SetItem(_node1, "b", GSet.Create("B"));

            var m2 = ORDictionary<string, GSet<string>>.Empty
                .SetItem(_node2, "c", GSet.Create("C"));

            var merged1 = m1.Merge(m2);
            var m3 = merged1.Remove(_node1, "b").SetItem(_node1, "b", GSet.Create("B2"));
            var merged2 = merged1.Merge(m3);

            Assert.Equal(ImmutableHashSet.Create("A"), merged2["a"].Elements);
            Assert.Equal(ImmutableHashSet.Create("B", "B2"), merged2["b"].Elements);
            Assert.Equal(ImmutableHashSet.Create("C"), merged2["c"].Elements);
        }

        [Fact]
        public void A_ORDictionary_should_not_allow_SetItem_for_ORSet_elements_type()
        {
            var m = ORDictionary<string, ORSet<string>>.Empty
                .SetItem(_node1, "a", ORSet.Create(_node1, "A"));

            Assert.Throws<ArgumentException>(() =>
                m.SetItem(_node1, "a", ORSet.Create(_node1, "B")));
        }

        [Fact]
        public void A_ORDictionary_should_be_able_to_update_an_entry()
        {
            var m1 = ORDictionary<string, ORSet<string>>.Empty
                .SetItem(_node1, "a", ORSet.Create(_node1, "A"))
                .SetItem(_node1, "b", ORSet.Create(_node1, "B01").Add(_node1, "B02").Add(_node1, "B03"));

            var m2 = ORDictionary<string, ORSet<string>>.Empty
                .SetItem(_node2, "c", ORSet.Create(_node2, "C"));

            var merged1 = m1.Merge(m2);

            var m3 = merged1.AddOrUpdate(_node1, "b", ORSet<string>.Empty, old => old.Clear(_node1).Add(_node1, "B2"));

            var merged2 = merged1.Merge(m3);
            Assert.Equal(ImmutableHashSet.Create("A"), merged2["a"].Elements);
            Assert.Equal(ImmutableHashSet.Create("B2"), merged2["b"].Elements);
            Assert.Equal(ImmutableHashSet.Create("C"), merged2["c"].Elements);

            var m4 = merged1.AddOrUpdate(_node2, "b", ORSet<string>.Empty, old => old.Add(_node2, "B3"));
            var merged3 = m3.Merge(m4);
            Assert.Equal(ImmutableHashSet.Create("A"), merged3["a"].Elements);
            Assert.Equal(ImmutableHashSet.Create("B2", "B3"), merged3["b"].Elements);
            Assert.Equal(ImmutableHashSet.Create("C"), merged3["c"].Elements);
        }

        [Fact]
        public void A_ORDictionary_should_be_able_to_update_ORSet_entry_with_Remove_then_Add()
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
        public void A_ORDictionary_should_be_able_to_update_ORSet_entry_with_Remove_then_Merge_then_Add()
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