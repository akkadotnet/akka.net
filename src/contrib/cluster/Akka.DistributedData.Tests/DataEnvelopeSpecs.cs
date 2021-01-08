//-----------------------------------------------------------------------
// <copyright file="DataEnvelopeSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using Akka.DistributedData.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.DistributedData.Tests
{
    public class DataEnvelopeSpec
    {
        private readonly UniqueAddress node1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2551), 1);
        private readonly UniqueAddress node2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2552), 1);
        private readonly UniqueAddress node3 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2553), 1);
        private readonly UniqueAddress node4 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2554), 1);

        private readonly DateTime obsoleteTimeInFuture = DateTime.UtcNow.AddHours(1);
        private readonly DateTime obsoleteTime = DateTime.UtcNow.AddHours(-1);

        [Fact]
        public void DataEnvelope_must_handle_pruning_transitions()
        {
            var g1 = GCounter.Empty.Increment(node1, 1);
            var d1 = new DataEnvelope(g1);

            var d2 = d1.InitRemovedNodePruning(node1, node2);
            d2.Pruning[node1].Should().BeOfType<PruningInitialized>();
            ((PruningInitialized)d2.Pruning[node1]).Owner.Should().Be(node2);

            // bug check for https://github.com/akkadotnet/akka.net/issues/4200
            var merged = d1.Merge(d2);
            merged.Data.As<GCounter>().Value.Should().Be(1);

            var d3 = d2.AddSeen(node3.Address);
            ((PruningInitialized)d3.Pruning[node1]).Seen.Should().BeEquivalentTo(node3.Address);

            var d4 = d3.Prune(node1, new PruningPerformed(obsoleteTimeInFuture));
            ((GCounter)d4.Data).ModifiedByNodes.Should().BeEquivalentTo(node2);

            // bug check for https://github.com/akkadotnet/akka.net/issues/4200
            var merged2 = d2.Merge(d4);
            merged2.Data.As<GCounter>().Value.Should().Be(1);
            merged2.NeedPruningFrom(node1).Should().BeFalse();
            merged2.Pruning[node1].Should().BeOfType<PruningPerformed>();
        }

        [Fact]
        public void DataEnvelope_must_merge_correctly()
        {
            var g1 = GCounter.Empty.Increment(node1, 1);
            var d1 = new DataEnvelope(g1);
            var g2 = GCounter.Empty.Increment(node2, 2);
            var d2 = new DataEnvelope(g2);

            var d3 = d1.Merge(d2);
            ((GCounter)d3.Data).Value.Should().Be(3);
            ((GCounter)d3.Data).ModifiedByNodes.Should().BeEquivalentTo(node1, node2);
            var d4 = d3.InitRemovedNodePruning(node1, node2);
            var d5 = d4.Prune(node1, new PruningPerformed(obsoleteTimeInFuture));
            ((GCounter)d5.Data).ModifiedByNodes.Should().BeEquivalentTo(node2);

            // late update from node 1
            var g11 = g1.Increment(node1, 10);
            var d6 = d5.Merge(new DataEnvelope(g11));
            ((GCounter)d6.Data).Value.Should().Be(3);
            ((GCounter)d6.Data).ModifiedByNodes.Should().BeEquivalentTo(node2);

            // remove obsolete
            var d7 = new DataEnvelope(d5.Data, d5.Pruning.SetItem(node1, new PruningPerformed(obsoleteTime)), d5.DeltaVersions);
            var d8 = new DataEnvelope(d5.Data, ImmutableDictionary<UniqueAddress, IPruningState>.Empty, d5.DeltaVersions);
            d8.Merge(d7).Pruning.Should().BeEmpty();
            d7.Merge(d8).Pruning.Should().BeEmpty();

            d5.Merge(d7).Pruning[node1].Should().Be(new PruningPerformed(obsoleteTimeInFuture));
            d7.Merge(d5).Pruning[node1].Should().Be(new PruningPerformed(obsoleteTimeInFuture));
        }

        /// <summary>
        /// Need to validate that DataEnvelope instances with identical content produce identical hashcodes, and vice versa.
        /// </summary>
        [Fact()]
        public void DataEnvelopes_with_identical_content_must_match()
        {
            var g1 = GCounter.Empty.Increment(node1, 1);
            var d1 = new DataEnvelope(g1);
            var d1a = new DataEnvelope(g1);
            var g2 = GCounter.Empty.Increment(node2, 2);
            var d2 = new DataEnvelope(g2);

            d1.Should().NotBe(d2);
            d1a.Should().Be(d1);
            d1a.GetHashCode().Should().Be(d1.GetHashCode());

            // add some delta versions
            var d1b = d1.WithDeltaVersions(VersionVector.Create(node1, 1));
            d1.Should().NotBe(d1b);
            d1.GetHashCode().Should().NotBe(d1b.GetHashCode());

            // add identical delta version on different instance
            var d1b2 = d1.WithDeltaVersions(VersionVector.Create(node1, 1));
            d1b2.Should().Be(d1b);
            d1b2.GetHashCode().Should().Be(d1b.GetHashCode());

            // merge with another instance
            var d2b1 = d1b2.Merge(d2);
            d2b1.Should().NotBe(d1b2);
            d2b1.GetHashCode().Should().NotBe(d1b2.GetHashCode());

            // prune
            var d3 = d2b1.WithPruning(
                ImmutableDictionary<UniqueAddress, IPruningState>.Empty.Add(node2, new PruningPerformed(obsoleteTime)));
            d3.Should().NotBe(d2b1);
            d3.GetHashCode().Should().NotBe(d2b1.GetHashCode());

            // create identical instance
            var d3b = d2b1.WithPruning(
                ImmutableDictionary<UniqueAddress, IPruningState>.Empty.Add(node2, new PruningPerformed(obsoleteTime)));
            d3.Should().Be(d3b);
            d3.GetHashCode().Should().Be(d3b.GetHashCode());
        }
    }
}
