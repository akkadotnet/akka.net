//-----------------------------------------------------------------------
// <copyright file="ORMultiDictionarySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using Xunit;
using Xunit.Abstractions;

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
        public void A_ORMultiDictionary_should_be_able_to_add_entries()
        {
            var m = ORMultiDictionary<string, string>.Empty.AddItem(_node1, "a", "A").AddItem(_node1, "b", "B");
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
        public void A_ORMultiDictionary_should_be_able_to_remove_entries()
        {
            var m = ORMultiDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A")
                .AddItem(_node1, "b", "B")
                .RemoveItem(_node1, "a", "A");

            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, IImmutableSet<string>>("b", ImmutableHashSet.Create("B"))
            }), m.Entries);
        }

        [Fact]
        public void A_ORMultiDictionary_should_be_able_to_replace_entries()
        {
            var m = ORMultiDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A")
                .ReplaceItem(_node1, "a", "A", "B");

            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create("B"))
            }), m.Entries);
        }

        [Fact]
        public void A_ORMultiDictionary_should_be_able_to_have_its_entries_correctly_merged_with_another_ORMultiDictionary_with_other_entries()
        {
            var m1 = ORMultiDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A")
                .AddItem(_node1, "b", "B");

            var m2 = ORMultiDictionary<string, string>.Empty
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
        public void A_ORMultiDictionary_should_be_able_to_have_its_entries_correctly_merged_with_another_ORMultiDictionary_with_overlaping_entries()
        {
            var m1 = ORMultiDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A1")
                .AddItem(_node1, "b", "B1")
                .RemoveItem(_node1, "a", "A1")
                .AddItem(_node1, "d", "D1");

            var m2 = ORMultiDictionary<string, string>.Empty
                .AddItem(_node2, "c", "C2")
                .AddItem(_node2, "a", "A2")
                .AddItem(_node2, "b", "B2")
                .RemoveItem(_node2, "b", "B2")
                .AddItem(_node2, "d", "D2");

            // merge both ways
            var expected = ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, IImmutableSet<string>>("a", ImmutableHashSet.Create("A2")),
                new KeyValuePair<string, IImmutableSet<string>>("b", ImmutableHashSet.Create("B1")),
                new KeyValuePair<string, IImmutableSet<string>>("c", ImmutableHashSet.Create("C2")),
                new KeyValuePair<string, IImmutableSet<string>>("d", ImmutableHashSet.Create("D1", "D2"))
            });

            var merged1 = m1.Merge(m2);
            Assert.Equal(expected, merged1.Entries);

            var merged2 = m2.Merge(m1);
            Assert.Equal(expected, merged2.Entries);
        }

        [Fact]
        public void A_ORMultiDictionary_should_be_able_to_get_all_bindings_for_an_entry_and_then_reduce_them_upon_putting_them_back()
        {
            var m = ORMultiDictionary<string, string>.Empty
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
        public void A_ORMultiDictionary_should_return_the_value_for_an_existing_key_and_the_default_for_a_non_existing_one()
        {
            var m = ORMultiDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A");

            IImmutableSet<string> v;
            Assert.True(m.TryGetValue("a", out v));
            Assert.Equal(ImmutableHashSet.Create("A"), v);

            Assert.False(m.TryGetValue("b", out v));
        }

        [Fact]
        public void A_ORMultiDictionary_should_remove_all_bindings_for_a_given_key()
        {
            var m = ORMultiDictionary<string, string>.Empty
                .AddItem(_node1, "a", "A1")
                .AddItem(_node1, "a", "A2")
                .AddItem(_node1, "b", "B1");

            var m2 = m.Remove(_node1, "a");
            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, IImmutableSet<string>>("b", ImmutableHashSet.Create("B1"))
            }), m2.Entries);
        }
    }
}