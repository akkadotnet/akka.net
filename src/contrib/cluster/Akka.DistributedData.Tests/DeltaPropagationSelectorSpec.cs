//-----------------------------------------------------------------------
// <copyright file="DeltaPropagationSelectorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster;
using Akka.DistributedData.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class DeltaPropagationSelectorSpec
    {
        private class TestSelector : DeltaPropagationSelector
        {
            private readonly UniqueAddress selfUniqueAddress;

            public TestSelector(UniqueAddress selfUniqueAddress, ImmutableArray<Address> allNodes)
            {
                this.selfUniqueAddress = selfUniqueAddress;
                AllNodes = allNodes;
            }

            public override int GossipInternalDivisor { get; } = 5;
            protected override ImmutableArray<Address> AllNodes { get; }
            protected override int MaxDeltaSize => 10;

            protected override DeltaPropagation CreateDeltaPropagation(ImmutableDictionary<string, (IReplicatedData, long, long)> deltas) =>
                new DeltaPropagation(selfUniqueAddress, false, deltas
                    .Select(kv => new KeyValuePair<string, Delta>(kv.Key, new Delta(new DataEnvelope(kv.Value.Item1), kv.Value.Item2, kv.Value.Item3)))
                    .ToImmutableDictionary());
        }

        private class TestSelector2 : TestSelector
        {
            public override int NodeSliceSize(int allNodesSize) => 1;

            public TestSelector2(UniqueAddress selfUniqueAddress, ImmutableArray<Address> allNodes) : base(selfUniqueAddress, allNodes)
            {
            }
        }

        private static readonly GSet<string> DeltaA = GSet<string>.Empty.Add("a");
        private static readonly GSet<string> DeltaB = GSet<string>.Empty.Add("b");
        private static readonly GSet<string> DeltaC = GSet<string>.Empty.Add("c");

        private readonly UniqueAddress selfUniqueAddress;
        private readonly ImmutableArray<Address> nodes;

        public DeltaPropagationSelectorSpec()
        {
            this.selfUniqueAddress = new UniqueAddress(new Address("akka", "Sys", "localhost", 4999), 1);
            this.nodes = Enumerable.Range(2500, 100)
                .Select(i => new Address("akka", "Sys", "localhost", i))
                .ToImmutableArray();
        }

        [Fact]
        public void DeltaPropagationSelector_must_collect_none_when_no_nodes()
        {
            var selector = new TestSelector(selfUniqueAddress, ImmutableArray<Address>.Empty);
            selector.Update("A", DeltaA);
            selector.CollectPropagations().Should().BeEmpty();
            selector.CleanupDeltaEntries();
            selector.HasDeltaEntries("A").Should().BeFalse();
        }

        [Fact]
        public void DeltaPropagationSelector_must_collect_1_when_one_node()
        {
            var selector = new TestSelector(selfUniqueAddress, nodes.Take(1).ToImmutableArray());
            selector.Update("A", DeltaA);
            selector.Update("B", DeltaB);
            selector.CleanupDeltaEntries();
            selector.HasDeltaEntries("A").Should().BeTrue();
            selector.HasDeltaEntries("B").Should().BeTrue();
            var expected = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("A", new Delta(new DataEnvelope(DeltaA), 1L, 1L))
                .Add("B", new Delta(new DataEnvelope(DeltaB), 1L, 1L)));
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[] { new KeyValuePair<Address, DeltaPropagation>(nodes[0], expected), }));
            selector.CollectPropagations().Should().BeEmpty();
            selector.CleanupDeltaEntries();
            selector.HasDeltaEntries("A").Should().BeFalse();
            selector.HasDeltaEntries("B").Should().BeFalse();
        }

        [Fact]
        public void DeltaPropagationSelector_must_collect_2_plus_1_when_three_node()
        {
            var selector = new TestSelector(selfUniqueAddress, nodes.Take(3).ToImmutableArray());
            selector.Update("A", DeltaA);
            selector.Update("B", DeltaB);
            var expected = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("A", new Delta(new DataEnvelope(DeltaA), 1L, 1L))
                .Add("B", new Delta(new DataEnvelope(DeltaB), 1L, 1L)));
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<Address, DeltaPropagation>(nodes[0], expected),
                new KeyValuePair<Address, DeltaPropagation>(nodes[1], expected),
            }));
            selector.CleanupDeltaEntries();
            selector.HasDeltaEntries("A").Should().BeTrue();
            selector.HasDeltaEntries("B").Should().BeTrue();
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[] { new KeyValuePair<Address, DeltaPropagation>(nodes[2], expected), }));
            selector.CollectPropagations().Should().BeEmpty();
            selector.CleanupDeltaEntries();
            selector.HasDeltaEntries("A").Should().BeFalse();
            selector.HasDeltaEntries("B").Should().BeFalse();
        }

        [Fact]
        public void DeltaPropagationSelector_must_keep_track_of_deltas_per_node()
        {
            var selector = new TestSelector(selfUniqueAddress, nodes.Take(3).ToImmutableArray());
            selector.Update("A", DeltaA);
            selector.Update("B", DeltaB);
            var expected = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("A", new Delta(new DataEnvelope(DeltaA), 1L, 1L))
                .Add("B", new Delta(new DataEnvelope(DeltaB), 1L, 1L)));
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<Address, DeltaPropagation>(nodes[0], expected),
                new KeyValuePair<Address, DeltaPropagation>(nodes[1], expected),
            }));
            // new update before previous was propagated to all nodes
            selector.Update("C", DeltaC);
            var expected2 = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("A", new Delta(new DataEnvelope(DeltaA), 1L, 1L))
                .Add("B", new Delta(new DataEnvelope(DeltaB), 1L, 1L))
                .Add("C", new Delta(new DataEnvelope(DeltaC), 1L, 1L)));
            var expected3 = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("C", new Delta(new DataEnvelope(DeltaC), 1L, 1L)));
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<Address, DeltaPropagation>(nodes[2], expected2),
                new KeyValuePair<Address, DeltaPropagation>(nodes[0], expected3),
            }));
            selector.CleanupDeltaEntries();
            selector.HasDeltaEntries("A").Should().BeFalse();
            selector.HasDeltaEntries("B").Should().BeFalse();
            selector.HasDeltaEntries("C").Should().BeTrue();
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[] { new KeyValuePair<Address, DeltaPropagation>(nodes[1], expected3), }));
            selector.CollectPropagations().Should().BeEmpty();
            selector.CleanupDeltaEntries();
            selector.HasDeltaEntries("C").Should().BeFalse();
        }

        [Fact]
        public void DeltaPropagationSelector_must_bump_version_for_each_update()
        {
            var delta1 = GSet<string>.Empty.Add("a1");
            var delta2 = GSet<string>.Empty.Add("a2");
            var delta3 = GSet<string>.Empty.Add("a3");

            var selector = new TestSelector(selfUniqueAddress, nodes.Take(1).ToImmutableArray());
            selector.Update("A", delta1);
            selector.CurrentVersion("A").Should().Be(1L);
            selector.Update("A", delta2);
            selector.CurrentVersion("A").Should().Be(2L);
            var expected1 = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("A", new Delta(new DataEnvelope(delta1.Merge(delta2)), 1L, 2L)));
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[] { new KeyValuePair<Address, DeltaPropagation>(nodes[0], expected1), }));
            selector.Update("A", delta3);
            selector.CurrentVersion("A").Should().Be(3L);
            var expected2 = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("A", new Delta(new DataEnvelope(delta3), 3L, 3L)));
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[] { new KeyValuePair<Address, DeltaPropagation>(nodes[0], expected2), }));
            selector.CollectPropagations().Should().BeEmpty();
        }

        [Fact]
        public void DeltaPropagationSelector_must_merge_deltas()
        {
            var delta1 = GSet<string>.Empty.Add("a1");
            var delta2 = GSet<string>.Empty.Add("a2");
            var delta3 = GSet<string>.Empty.Add("a3");

            var selector = new TestSelector2(selfUniqueAddress, nodes.Take(3).ToImmutableArray());

            selector.Update("A", delta1);
            var expected1 = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("A", new Delta(new DataEnvelope(delta1), 1L, 1L)));
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[] { new KeyValuePair<Address, DeltaPropagation>(nodes[0], expected1), }));

            selector.Update("A", delta2);
            var expected2 = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("A", new Delta(new DataEnvelope(delta1.Merge(delta2)), 1L, 2L)));
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[] { new KeyValuePair<Address, DeltaPropagation>(nodes[1], expected2), }));

            selector.Update("A", delta3);
            var expected3 = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("A", new Delta(new DataEnvelope(delta1.Merge(delta2).Merge(delta3)), 1L, 3L)));
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[] { new KeyValuePair<Address, DeltaPropagation>(nodes[2], expected3), }));

            var expected4 = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("A", new Delta(new DataEnvelope(delta2.Merge(delta3)), 2L, 3L)));
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[] { new KeyValuePair<Address, DeltaPropagation>(nodes[0], expected4), }));

            var expected5 = new DeltaPropagation(selfUniqueAddress, false, ImmutableDictionary<string, Delta>.Empty
                .Add("A", new Delta(new DataEnvelope(delta3), 3L, 3L)));
            selector.CollectPropagations().Should().Equal(ImmutableDictionary.CreateRange(new[] { new KeyValuePair<Address, DeltaPropagation>(nodes[1], expected5), }));

            selector.CollectPropagations().Should().BeEmpty();
        }

        [Fact]
        public void DeltaPropagationSelector_must_calculate_right_slice_size()
        {
            var selector = new TestSelector(selfUniqueAddress, nodes);
            selector.NodeSliceSize(0).Should().Be(0);
            selector.NodeSliceSize(1).Should().Be(1);

            for (int i = 2; i < 10; i++) selector.NodeSliceSize(i).Should().Be(2);
            for (int i = 10; i < 15; i++) selector.NodeSliceSize(i).Should().Be(3);
            for (int i = 15; i < 20; i++) selector.NodeSliceSize(i).Should().Be(4);
            for (int i = 20; i < 25; i++) selector.NodeSliceSize(i).Should().Be(5);
            for (int i = 25; i < 30; i++) selector.NodeSliceSize(i).Should().Be(6);
            for (int i = 30; i < 35; i++) selector.NodeSliceSize(i).Should().Be(7);
            for (int i = 35; i < 40; i++) selector.NodeSliceSize(i).Should().Be(8);
            for (int i = 40; i < 45; i++) selector.NodeSliceSize(i).Should().Be(9);
            for (int i = 45; i < 200; i++) selector.NodeSliceSize(i).Should().Be(10);
        }

        [Fact]
        public void DeltaPropagationSelector_must_discard_too_large_deltas()
        {
            var selector = new TestSelector2(selfUniqueAddress, nodes.Take(3).ToImmutableArray());
            var data = PNCounterDictionary<string>.Empty;
            for (int i = 1; i <= 1000; i++)
            {
                var d = data.ResetDelta().Increment(selfUniqueAddress, (i % 2).ToString(), 1);
                selector.Update("A", d.Delta);
                data = d;
            }

            var expected = new DeltaPropagation(selfUniqueAddress, false, new Dictionary<string, Delta>
            {
                { "A", new Delta(new DataEnvelope(DeltaPropagation.NoDeltaPlaceholder), 1L, 1000L) }
            }.ToImmutableDictionary());
            selector.CollectPropagations().Should().Equal(new Dictionary<Address, DeltaPropagation>
            {
                { nodes[0], expected }
            });
        }
    }
}
