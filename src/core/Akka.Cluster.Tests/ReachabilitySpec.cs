//-----------------------------------------------------------------------
// <copyright file="ReachabilitySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class ReachabilitySpec
    {
        static readonly UniqueAddress nodeA = new UniqueAddress(new Address("akka.tcp", "sys", "a", 2552), 1);
        static readonly UniqueAddress nodeB = new UniqueAddress(new Address("akka.tcp", "sys", "b", 2552), 2);
        static readonly UniqueAddress nodeC = new UniqueAddress(new Address("akka.tcp", "sys", "c", 2552), 3);
        static readonly UniqueAddress nodeD = new UniqueAddress(new Address("akka.tcp", "sys", "d", 2552), 4);
        static readonly UniqueAddress nodeE = new UniqueAddress(new Address("akka.tcp", "sys", "e", 2552), 5);

        [Fact]
        public void ReachabilityTable_must_be_reachable_when_empty()
        {
            var r = Reachability.Empty;
            r.IsReachable(nodeA).Should().BeTrue();
            r.AllUnreachable.Should().BeEquivalentTo(ImmutableHashSet.Create<UniqueAddress>());
        }

        [Fact]
        public void ReachabilityTable_must_be_unreachable_when_one_observed_unreachable()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA);
            Assert.False(r.IsReachable(nodeA));
            Assert.Equal(ImmutableHashSet.Create(nodeA), r.AllUnreachable);
        }

        [Fact]
        public void ReachabilityTable_must_not_be_reachable_when_terminated()
        {
            var r = Reachability.Empty.Terminated(nodeB, nodeA);
            r.IsReachable(nodeA).Should().BeFalse();
            // allUnreachable doesn't include terminated
            r.AllUnreachable.Should().BeEquivalentTo(ImmutableHashSet.Create<UniqueAddress>());
            r.AllUnreachableOrTerminated.Should().BeEquivalentTo(ImmutableHashSet.Create(nodeA));
        }

        [Fact]
        public void ReachabilityTable_must_not_change_terminated_entry()
        {
            var r = Reachability.Empty.Terminated(nodeB, nodeA);
            r.Reachable(nodeB, nodeA).Should().BeSameAs(r);
            r.Unreachable(nodeB, nodeA).Should().BeSameAs(r);
        }

        [Fact]
        public void ReachabilityTable_must_not_change_when_same_status()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA);
            r.Unreachable(nodeB, nodeA).Should().BeSameAs(r);
        }

        [Fact]
        public void ReachabilityTable_must_be_unreachable_when_some_observed_unreachable_and_other_reachable()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeA).Reachable(nodeD, nodeA);
            r.IsReachable(nodeA).Should().BeFalse();
        }

        [Fact]
        public void ReachabilityTable_must_be_reachable_when_all_observed_reachable_again()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeA).
                Reachable(nodeB, nodeA).Reachable(nodeC, nodeA).
                Unreachable(nodeB, nodeC).Unreachable(nodeC, nodeB);
            r.IsReachable(nodeA).Should().BeTrue();
        }

        [Fact]
        public void ReachabilityTable_must_exclude_observations_from_specific_downed_nodes()
        {
            var r = Reachability.Empty.
                Unreachable(nodeC, nodeA).Reachable(nodeC, nodeA).
                Unreachable(nodeC, nodeB).
                Unreachable(nodeB, nodeA).Unreachable(nodeB, nodeC);

            r.IsReachable(nodeA).Should().BeFalse();
            r.IsReachable(nodeB).Should().BeFalse();
            r.IsReachable(nodeC).Should().BeFalse();
            r.AllUnreachableOrTerminated.Should().BeEquivalentTo(ImmutableHashSet.Create(nodeA, nodeB, nodeC));
            r.RemoveObservers(ImmutableHashSet.Create(nodeB)).AllUnreachableOrTerminated.Should().BeEquivalentTo(ImmutableHashSet.Create(nodeB));
        }

        [Fact]
        public void ReachabilityTable_must_be_pruned_when_all_records_of_an_observer_are_reachable()
        {
            var r = Reachability.Empty.
                Unreachable(nodeB, nodeA).Unreachable(nodeB, nodeC).
                Unreachable(nodeD, nodeC).
                Reachable(nodeB, nodeA).Reachable(nodeB, nodeC);

            r.IsReachable(nodeA).Should().BeTrue();
            r.IsReachable(nodeC).Should().BeFalse();

            var expected1 = ImmutableList.Create(
                new Reachability.Record(nodeD, nodeC, Reachability.ReachabilityStatus.Unreachable, 1));
            r.Records.Should().BeEquivalentTo(expected1);

            var r2 = r.Unreachable(nodeB, nodeD).Unreachable(nodeB, nodeE);
            var expected2 = ImmutableHashSet.Create(
                new Reachability.Record(nodeD, nodeC, Reachability.ReachabilityStatus.Unreachable, 1),
                new Reachability.Record(nodeB, nodeD, Reachability.ReachabilityStatus.Unreachable, 5),
                new Reachability.Record(nodeB, nodeE, Reachability.ReachabilityStatus.Unreachable, 6));

            r2.Records.ToImmutableHashSet().Should().BeEquivalentTo(expected2);
        }

        [Fact]
        public void ReachabilityTable_must_have_correct_aggregated_status()
        {
            var records = ImmutableList.Create(
                new Reachability.Record(nodeA, nodeB, Reachability.ReachabilityStatus.Reachable, 2),
                new Reachability.Record(nodeC, nodeB, Reachability.ReachabilityStatus.Unreachable, 2),
                new Reachability.Record(nodeA, nodeD, Reachability.ReachabilityStatus.Unreachable, 3),
                new Reachability.Record(nodeD, nodeB, Reachability.ReachabilityStatus.Terminated, 4));

            var versions = ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<UniqueAddress, long>(nodeA, 3),
                new KeyValuePair<UniqueAddress, long>(nodeC, 3),
                new KeyValuePair<UniqueAddress, long>(nodeD, 4)
            });
            var r = new Reachability(records, versions);
            r.Status(nodeA).Should().Be(Reachability.ReachabilityStatus.Reachable);
            r.Status(nodeB).Should().Be(Reachability.ReachabilityStatus.Terminated);
            r.Status(nodeD).Should().Be(Reachability.ReachabilityStatus.Unreachable);
        }

        [Fact]
        public void ReachabilityTable_must_have_correct_status_for_a_mix_of_nodes()
        {
            var r = Reachability.Empty.
                Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeA).Unreachable(nodeD, nodeA).
                Unreachable(nodeC, nodeB).Reachable(nodeC, nodeB).Unreachable(nodeD, nodeB).
                Unreachable(nodeD, nodeC).Reachable(nodeD, nodeC).
                Reachable(nodeE, nodeD).
                Unreachable(nodeA, nodeE).Terminated(nodeB, nodeE);

            r.Status(nodeB, nodeA).Should().Be(Reachability.ReachabilityStatus.Unreachable);
            r.Status(nodeC, nodeA).Should().Be(Reachability.ReachabilityStatus.Unreachable);
            r.Status(nodeD, nodeA).Should().Be(Reachability.ReachabilityStatus.Unreachable);

            r.Status(nodeC, nodeB).Should().Be(Reachability.ReachabilityStatus.Reachable);
            r.Status(nodeD, nodeB).Should().Be(Reachability.ReachabilityStatus.Unreachable);

            r.Status(nodeA, nodeE).Should().Be(Reachability.ReachabilityStatus.Unreachable);
            r.Status(nodeB, nodeE).Should().Be(Reachability.ReachabilityStatus.Terminated);

            r.IsReachable(nodeA).Should().BeFalse();
            r.IsReachable(nodeB).Should().BeFalse();
            r.IsReachable(nodeC).Should().BeTrue();
            r.IsReachable(nodeD).Should().BeTrue();
            r.IsReachable(nodeE).Should().BeFalse();

            r.AllUnreachable.Should().BeEquivalentTo(ImmutableHashSet.Create(nodeA, nodeB));
            r.AllUnreachableFrom(nodeA).Should().BeEquivalentTo(ImmutableHashSet.Create(nodeE));
            r.AllUnreachableFrom(nodeB).Should().BeEquivalentTo(ImmutableHashSet.Create(nodeA));
            r.AllUnreachableFrom(nodeC).Should().BeEquivalentTo(ImmutableHashSet.Create(nodeA));
            r.AllUnreachableFrom(nodeD).Should().BeEquivalentTo(ImmutableHashSet.Create(nodeA, nodeB));

            var expected = new Dictionary<UniqueAddress, ImmutableHashSet<UniqueAddress>>
            {
                {nodeA, ImmutableHashSet.Create(nodeB, nodeC, nodeD)},
                {nodeB, ImmutableHashSet.Create(nodeD)},
                {nodeE, ImmutableHashSet.Create(nodeA)}
            }.ToImmutableDictionary();

            r.ObserversGroupedByUnreachable.Should().HaveCount(3);
            r.ObserversGroupedByUnreachable[nodeA].Should().BeEquivalentTo(expected[nodeA]);
            r.ObserversGroupedByUnreachable[nodeB].Should().BeEquivalentTo(expected[nodeB]);
            r.ObserversGroupedByUnreachable[nodeE].Should().BeEquivalentTo(expected[nodeE]);
        }

        [Fact]
        public void ReachabilityTable_must_merge_by_picking_latest_version_of_each_record()
        {
            var r1 = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeD);
            var r2 = r1.Reachable(nodeB, nodeA).Unreachable(nodeD, nodeE).Unreachable(nodeC, nodeA);
            var merged = r1.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r2);

            merged.Status(nodeB, nodeA).Should().Be(Reachability.ReachabilityStatus.Reachable);
            merged.Status(nodeC, nodeA).Should().Be(Reachability.ReachabilityStatus.Unreachable);
            merged.Status(nodeC, nodeD).Should().Be(Reachability.ReachabilityStatus.Unreachable);
            merged.Status(nodeD, nodeE).Should().Be(Reachability.ReachabilityStatus.Unreachable);
            merged.Status(nodeE, nodeA).Should().Be(Reachability.ReachabilityStatus.Reachable);

            merged.IsReachable(nodeA).Should().BeFalse();
            merged.IsReachable(nodeD).Should().BeFalse();
            merged.IsReachable(nodeE).Should().BeFalse();

            var merged2 = r2.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r1);
            merged2.Records.ToImmutableHashSet().Should().BeEquivalentTo(merged.Records.ToImmutableHashSet());
        }

        [Fact]
        public void ReachabilityTable_must_merge_by_taking_allowed_set_into_account()
        {
            var r1 = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeD);
            var r2 = r1.Reachable(nodeB, nodeA).Unreachable(nodeD, nodeE).Unreachable(nodeC, nodeA);
            // nodeD not in allowed set
            var allowed = ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeE);
            var merged = r1.Merge(allowed, r2);

            merged.Status(nodeB, nodeA).Should().Be(Reachability.ReachabilityStatus.Reachable);
            merged.Status(nodeC, nodeA).Should().Be(Reachability.ReachabilityStatus.Unreachable);
            merged.Status(nodeC, nodeD).Should().Be(Reachability.ReachabilityStatus.Reachable);
            merged.Status(nodeD, nodeE).Should().Be(Reachability.ReachabilityStatus.Reachable);
            merged.Status(nodeE, nodeA).Should().Be(Reachability.ReachabilityStatus.Reachable);

            merged.IsReachable(nodeA).Should().BeFalse();
            merged.IsReachable(nodeD).Should().BeTrue();
            merged.IsReachable(nodeE).Should().BeTrue();

            merged.Versions.Keys.Should().BeEquivalentTo(ImmutableHashSet.Create(nodeB, nodeC));

            var merged2 = r2.Merge(allowed, r1);
            merged2.Records.ToImmutableHashSet().Should().BeEquivalentTo(merged.Records.ToImmutableHashSet());
            merged2.Versions.Should().Equal(merged.Versions);
        }

        [Fact]
        public void ReachabilityTable_must_merge_correctly_after_pruning()
        {
            var r1 = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeD);
            var r2 = r1.Unreachable(nodeA, nodeE);
            var r3 = r1.Reachable(nodeB, nodeA); //nodeB pruned
            var merged = r2.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r3);

            var expected = ImmutableHashSet.Create(
                new Reachability.Record(nodeA, nodeE, Reachability.ReachabilityStatus.Unreachable, 1),
                new Reachability.Record(nodeC, nodeD, Reachability.ReachabilityStatus.Unreachable, 1));
            merged.Records.ToImmutableHashSet().Should().BeEquivalentTo(expected);

            var merged3 = r3.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r2);
            merged3.Records.ToImmutableHashSet().Should().BeEquivalentTo(merged.Records.ToImmutableHashSet());
        }

        [Fact]
        public void ReachabilityTable_must_merge_versions_correctly()
        {
            var r1 = new Reachability(ImmutableList.Create<Reachability.Record>(), new Dictionary<UniqueAddress, long>
            {
                { nodeA, 3 },
                { nodeB, 5 },
                { nodeC, 7 }
            }.ToImmutableDictionary());

            var r2 = new Reachability(ImmutableList.Create<Reachability.Record>(), new Dictionary<UniqueAddress, long>
            {
                { nodeA, 6 },
                { nodeB, 2 },
                { nodeD, 1 }
            }.ToImmutableDictionary());
            var merged = r1.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r2);

            var expected = new Dictionary<UniqueAddress, long>
            {
                { nodeA, 6 },
                { nodeB, 5 },
                { nodeC, 7 },
                { nodeD, 1 }
            }.ToImmutableDictionary();
            merged.Versions.Should().Equal(expected);

            var merged2 = r2.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r1);
            merged2.Versions.Should().Equal(expected);
        }

        [Fact]
        public void ReachabilityTable_must_remove_node()
        {
            var r = Reachability.Empty.
                Unreachable(nodeB, nodeA).
                Unreachable(nodeC, nodeD).
                Unreachable(nodeB, nodeC).
                Unreachable(nodeB, nodeE).
                Remove(ImmutableHashSet.Create(nodeA, nodeB));

            r.Status(nodeB, nodeA).Should().Be(Reachability.ReachabilityStatus.Reachable);
            r.Status(nodeC, nodeD).Should().Be(Reachability.ReachabilityStatus.Unreachable);
            r.Status(nodeB, nodeC).Should().Be(Reachability.ReachabilityStatus.Reachable);
            r.Status(nodeB, nodeE).Should().Be(Reachability.ReachabilityStatus.Reachable);
        }

        [Fact]
        public void ReachabilityTable_must_remove_correctly_after_pruning()
        {
            var r = Reachability.Empty.
                Unreachable(nodeB, nodeA).
                Unreachable(nodeB, nodeC).
                Unreachable(nodeD, nodeC).
                Reachable(nodeB, nodeA).
                Reachable(nodeB, nodeC);

            r.Records.Should().BeEquivalentTo(ImmutableList.Create(
                new Reachability.Record(nodeD, nodeC, Reachability.ReachabilityStatus.Unreachable, 1L)));

            var r2 = r.Remove(ImmutableList.Create(nodeB));
            r2.AllObservers.Should().BeEquivalentTo(ImmutableList.Create(nodeD));
            r2.Versions.Keys.Should().BeEquivalentTo(ImmutableList.Create(nodeD));
        }
    }
}
