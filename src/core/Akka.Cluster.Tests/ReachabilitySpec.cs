//-----------------------------------------------------------------------
// <copyright file="ReachabilitySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
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
        public void ReachabilityTableMustBeReachableWhenEmpty()
        {
            var r = Reachability.Empty;
            Assert.True(r.IsReachable(nodeA));
            Assert.Equal(ImmutableHashSet.Create<UniqueAddress>(), r.AllUnreachable);
        }

        [Fact]
        public void ReachabilityTableMustBeUnreachableWhenOneObservedUnreachable()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA);
            Assert.False(r.IsReachable(nodeA));
            Assert.Equal(ImmutableHashSet.Create(nodeA), r.AllUnreachable);
        }

        [Fact]
        public void ReachabilityTableMustNotBeReachableWhenTerminated()
        {
            var r = Reachability.Empty.Terminated(nodeB, nodeA);
            Assert.False(r.IsReachable(nodeA));
            // allUnreachable doesn't include terminated
            Assert.Equal(ImmutableHashSet.Create<UniqueAddress>(), r.AllUnreachable);
            Assert.Equal(ImmutableHashSet.Create(nodeA), r.AllUnreachableOrTerminated);
        }

        [Fact]
        public void ReachabilityTableMustNotChangeTerminatedEntry()
        {
            var r = Reachability.Empty.Terminated(nodeB, nodeA);
            Assert.True(r == r.Reachable(nodeB, nodeA));
            Assert.True(r == r.Unreachable(nodeB, nodeA));
        }

        [Fact]
        public void ReachabilityTableMustNotChangeWhenSameStatus()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA);
            Assert.True(r == r.Unreachable(nodeB, nodeA));
        }

        [Fact]
        public void ReachabilityTableMustBeUnreachableWhenSomeObservedUnreachableAndOtherReachable()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeA).Reachable(nodeD, nodeA);
            Assert.False(r.IsReachable(nodeA));
        }

        [Fact]
        public void ReachabilityTableMustReachableWhenAllObservedReachableAgain()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeA).
                Reachable(nodeB, nodeA).Reachable(nodeC, nodeA).
                Unreachable(nodeB, nodeC).Unreachable(nodeC, nodeB);
            Assert.True(r.IsReachable(nodeA));
        }

        [Fact]
        public void ReachabilityTableMustBePrunedWhenAllRecordsOfAnObserverAreReachable()
        {
            var r = Reachability.Empty.
                Unreachable(nodeB, nodeA).Unreachable(nodeB, nodeC).
                Unreachable(nodeD, nodeC).
                Reachable(nodeB, nodeA).Reachable(nodeB, nodeC);
            Assert.True(r.IsReachable(nodeA));
            Assert.False(r.IsReachable(nodeC));
            var expected1 = ImmutableList.Create(new Reachability.Record(
                nodeD,
                nodeC,
                Reachability.ReachabilityStatus.Unreachable,
                1));
            Assert.Equal(expected1, r.Records);

            var r2 = r.Unreachable(nodeB, nodeD).Unreachable(nodeB, nodeE);
            var expected2 = ImmutableHashSet.Create(
                new Reachability.Record(nodeD, nodeC, Reachability.ReachabilityStatus.Unreachable, 1),
                new Reachability.Record(nodeB, nodeD, Reachability.ReachabilityStatus.Unreachable, 5),
                new Reachability.Record(nodeB, nodeE, Reachability.ReachabilityStatus.Unreachable, 6)
                );
            Assert.Equal(expected2, r2.Records.ToImmutableHashSet());
        }

        [Fact]
        public void ReachabilityTableMustHaveCorrectAggregatedStatus()
        {
            var records = ImmutableList.Create(
                new Reachability.Record(nodeA, nodeB, Reachability.ReachabilityStatus.Reachable, 2),
                new Reachability.Record(nodeC, nodeB, Reachability.ReachabilityStatus.Unreachable, 2),
                new Reachability.Record(nodeA, nodeD, Reachability.ReachabilityStatus.Unreachable, 3),
                new Reachability.Record(nodeD, nodeB, Reachability.ReachabilityStatus.Terminated, 4)
                );
            var versions = ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<UniqueAddress, long>(nodeA, 3),
                new KeyValuePair<UniqueAddress, long>(nodeC, 3),
                new KeyValuePair<UniqueAddress, long>(nodeD, 4)
            });
            var r = new Reachability(records, versions);
            Assert.Equal(Reachability.ReachabilityStatus.Reachable, r.Status(nodeA));
            Assert.Equal(Reachability.ReachabilityStatus.Terminated, r.Status(nodeB));
            Assert.Equal(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeD));
        }

        [Fact]
        public void ReachabilityTableMustHaveCorrectStatusForAMixOfNodes()
        {
            var r = Reachability.Empty.
                Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeA).Unreachable(nodeD, nodeA).
                Unreachable(nodeC, nodeB).Reachable(nodeC, nodeB).Unreachable(nodeD, nodeB).
                Unreachable(nodeD, nodeC).Reachable(nodeD, nodeC).
                Reachable(nodeE, nodeD).
                Unreachable(nodeA, nodeE).Terminated(nodeB, nodeE);

            Assert.Equal(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeB, nodeA));
            Assert.Equal(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeC, nodeA));
            Assert.Equal(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeD, nodeA));

            Assert.Equal(Reachability.ReachabilityStatus.Reachable, r.Status(nodeC, nodeB));
            Assert.Equal(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeD, nodeB));

            Assert.Equal(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeA, nodeE));
            Assert.Equal(Reachability.ReachabilityStatus.Terminated, r.Status(nodeB, nodeE));

            Assert.False(r.IsReachable(nodeA));
            Assert.False(r.IsReachable(nodeB));
            Assert.True(r.IsReachable(nodeC));
            Assert.True(r.IsReachable(nodeD));
            Assert.False(r.IsReachable(nodeE));

            Assert.Equal(ImmutableHashSet.Create(nodeA, nodeB), r.AllUnreachable);
            Assert.Equal(ImmutableHashSet.Create(nodeE), r.AllUnreachableFrom(nodeA));
            Assert.Equal(ImmutableHashSet.Create(nodeA), r.AllUnreachableFrom(nodeB));
            Assert.Equal(ImmutableHashSet.Create(nodeA), r.AllUnreachableFrom(nodeC));
            Assert.Equal(ImmutableHashSet.Create(nodeA, nodeB), r.AllUnreachableFrom(nodeD));

            var expected = new Dictionary<UniqueAddress, ImmutableHashSet<UniqueAddress>>
            {
                {nodeA, ImmutableHashSet.Create(nodeB, nodeC, nodeD)},
                {nodeB, ImmutableHashSet.Create(nodeD)},
                {nodeE, ImmutableHashSet.Create(nodeA)}
            }.ToImmutableDictionary();

            Assert.Equal<KeyValuePair<UniqueAddress, ImmutableHashSet<UniqueAddress>>>(expected, r.ObserversGroupedByUnreachable, new DictionaryOfHashsetComparer());
        }

        class DictionaryOfHashsetComparer : IEqualityComparer<KeyValuePair<UniqueAddress, ImmutableHashSet<UniqueAddress>>>
        {
            public bool Equals(KeyValuePair<UniqueAddress, ImmutableHashSet<UniqueAddress>> x, KeyValuePair<UniqueAddress, ImmutableHashSet<UniqueAddress>> y)
            {
                 if (!x.Key.Equals(y.Key)) return false;

                if (!x.Value.SequenceEqual(y.Value)) return false;
                return true;
            }

            public int GetHashCode(KeyValuePair<UniqueAddress, ImmutableHashSet<UniqueAddress>> obj)
            {
                return obj.GetHashCode();
            }
        }

        [Fact]
        public void ReachabilityTableMustMergeByTakingAllowedSetIntoAccount()
        {
            var r1 = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeD);
            var r2 = r1.Reachable(nodeB, nodeA).Unreachable(nodeD, nodeE).Unreachable(nodeC, nodeA);
            // nodeD not in allowed set
            var allowed = ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeE);
            var merged = r1.Merge(allowed, r2);

            Assert.Equal(Reachability.ReachabilityStatus.Reachable, merged.Status(nodeB, nodeA));
            Assert.Equal(Reachability.ReachabilityStatus.Unreachable, merged.Status(nodeC, nodeA));
            Assert.Equal(Reachability.ReachabilityStatus.Reachable, merged.Status(nodeC, nodeD));
            Assert.Equal(Reachability.ReachabilityStatus.Reachable, merged.Status(nodeD, nodeE));
            Assert.Equal(Reachability.ReachabilityStatus.Reachable, merged.Status(nodeE, nodeA));

            Assert.False(merged.IsReachable(nodeA));
            Assert.True(merged.IsReachable(nodeD));
            Assert.True(merged.IsReachable(nodeE));

            Assert.Equal(ImmutableHashSet.Create(nodeB, nodeC), merged.Versions.Keys.ToImmutableHashSet());

            var merged2 = r2.Merge(allowed, r1);
            Assert.Equal(merged.Records.ToImmutableHashSet(), merged2.Records.ToImmutableHashSet());
            Assert.Equal(merged.Versions, merged2.Versions);
        }

        [Fact]
        public void ReachabilityTableMustMergeCorrectlyAfterPruning()
        {
            var r1 = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeD);
            var r2 = r1.Unreachable(nodeA, nodeE);
            var r3 = r1.Reachable(nodeB, nodeA); //nodeB pruned
            var merged = r2.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r3);

            Assert.Equal(ImmutableHashSet.Create(
                new Reachability.Record(nodeA, nodeE, Reachability.ReachabilityStatus.Unreachable, 1),
                new Reachability.Record(nodeC, nodeD, Reachability.ReachabilityStatus.Unreachable, 1))
                , merged.Records.ToImmutableHashSet());

            var merged3 = r3.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r2);
            Assert.Equal(merged.Records.ToImmutableHashSet(), merged3.Records.ToImmutableHashSet());
        }

        [Fact]
        public void ReachabilityTableMustMergeVersionsCorrectly()
        {
            var r1 = new Reachability(ImmutableList.Create<Reachability.Record>(),
                new Dictionary<UniqueAddress, long> {{nodeA, 3}, {nodeB, 5}, {nodeC, 7}}.ToImmutableDictionary());
            var r2 = new Reachability(ImmutableList.Create<Reachability.Record>(),
                new Dictionary<UniqueAddress, long> { { nodeA, 6 }, { nodeB, 2 }, { nodeD, 1 } }.ToImmutableDictionary());
            var merged = r1.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r2);

            var expected = new Dictionary<UniqueAddress, long> { { nodeA, 6 }, { nodeB, 5 }, { nodeC, 7 }, { nodeD, 1} }.ToImmutableDictionary();
            Assert.Equal(expected, merged.Versions);

            var merged2 = r2.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r1);
            Assert.Equal(expected, merged2.Versions);
        }

        [Fact]
        public void ReachabilityTableMustRemoveNode()
        {
            var r = Reachability.Empty.
                Unreachable(nodeB, nodeA).
                Unreachable(nodeC, nodeD).
                Unreachable(nodeB, nodeC).
                Unreachable(nodeB, nodeE).
                Remove(ImmutableHashSet.Create(nodeA, nodeB));

            Assert.Equal(Reachability.ReachabilityStatus.Reachable, r.Status(nodeB, nodeA));
            Assert.Equal(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeC, nodeD));
            Assert.Equal(Reachability.ReachabilityStatus.Reachable, r.Status(nodeB, nodeC));
            Assert.Equal(Reachability.ReachabilityStatus.Reachable, r.Status(nodeB, nodeE));
        }
    }
}
