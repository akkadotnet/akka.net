﻿//-----------------------------------------------------------------------
// <copyright file="PNCounterDictionarySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using Akka.Cluster;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class PNCounterDictionarySpec
    {
        readonly UniqueAddress _node1;
        readonly UniqueAddress _node2;

        public PNCounterDictionarySpec(ITestOutputHelper output)
        {
            _node1 = new UniqueAddress(new Actor.Address("akka.tcp", "Sys", "localhost", 2551), 1);
            _node2 = new UniqueAddress(new Actor.Address("akka.tcp", "Sys", "localhost", 2552), 2);
        }

        [Fact]
        public void PNCounterDictionary_must_be_able_to_increment_and_decrement_entries()
        {
            var m = PNCounterDictionary<string>.Empty
                .Increment(_node1, "a", 2)
                .Increment(_node1, "b", 3)
                .Decrement(_node2, "a", 1);

            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, BigInteger>("a", new BigInteger(1)),
                new KeyValuePair<string, BigInteger>("b", new BigInteger(3))
            }), m.Entries);
        }

        [Fact]
        public void PNCounterDictionary_must_be_able_to_have_its_entries_correctly_merged_with_another_ORDictionary_with_other_entries()
        {
            var m1 = PNCounterDictionary<string>.Empty
                .Increment(_node1, "a", 1)
                .Increment(_node1, "b", 3)
                .Increment(_node1, "c", 2);

            var m2 = PNCounterDictionary<string>.Empty
                .Increment(_node2, "c", 5);

            // merge both ways
            var expected = ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, BigInteger>("a", new BigInteger(1)),
                new KeyValuePair<string, BigInteger>("b", new BigInteger(3)),
                new KeyValuePair<string, BigInteger>("c", new BigInteger(7))
            });
            Assert.Equal(expected, m1.Merge(m2).Entries);
            Assert.Equal(expected, m2.Merge(m1).Entries);
        }

        [Fact]
        public void PNCounterDictionary_must_be_able_to_remove_entry()
        {
            var m1 = PNCounterDictionary<string>.Empty
                .Increment(_node1, "a", 1)
                .Increment(_node1, "b", 3)
                .Increment(_node1, "c", 2);

            var m2 = PNCounterDictionary<string>.Empty
                .Increment(_node2, "c", 5);

            var merged1 = m1.Merge(m2);

            var m3 = merged1.Remove(_node1, "b");
            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, BigInteger>("a", new BigInteger(1)),
                new KeyValuePair<string, BigInteger>("c", new BigInteger(7))
            }), merged1.Merge(m3).Entries);

            // but if there is a conflicting update the entry is not removed
            var m4 = merged1.Increment(_node2, "b", 10);
            Assert.Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, BigInteger>("a", new BigInteger(1)),
                new KeyValuePair<string, BigInteger>("b", new BigInteger(13)),
                new KeyValuePair<string, BigInteger>("c", new BigInteger(7))
            }), m3.Merge(m4).Entries);
        }

        [Fact]
        public void PNCounterDictionary_must_be_able_to_work_with_deltas()
        {
            var m1 = PNCounterDictionary<string>.Empty
                .Increment(_node1, "a", 1)
                .Increment(_node1, "b", 3)
                .Increment(_node1, "c", 2);
            var m2 = PNCounterDictionary<string>.Empty.Increment(_node2, "c", 5);

            var expected = new Dictionary<string, BigInteger>
            {
                { "a", 1 },
                { "b", 3 },
                { "c", 7 },
            }.ToImmutableDictionary();
            PNCounterDictionary<string>.Empty.MergeDelta(m1.Delta).MergeDelta(m2.Delta).Entries.Should().BeEquivalentTo(expected);
            PNCounterDictionary<string>.Empty.MergeDelta(m2.Delta).MergeDelta(m1.Delta).Entries.Should().BeEquivalentTo(expected);

            var merged1 = m1.Merge(m2);

            var m3 = merged1.ResetDelta().Remove(_node1, "b");
            merged1.MergeDelta(m3.Delta).Entries.Should().BeEquivalentTo(new Dictionary<string, BigInteger>
            {
                { "a", 1 },
                { "c", 7 },
            });

            // but if there is a conflicting update the entry is not removed
            var m4 = merged1.ResetDelta().Increment(_node2, "b", 10);
            m3.MergeDelta(m4.Delta).Entries.Should().BeEquivalentTo(new Dictionary<string, BigInteger>
            {
                { "a", 1 },
                { "b", 13 },
                { "c", 7 },
            });
        }
    }
}