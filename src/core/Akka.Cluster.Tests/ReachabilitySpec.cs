using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Cluster.Tests
{
    [TestClass]
    public class ReachabilitySpec
    {
        static readonly UniqueAddress nodeA = new UniqueAddress(new Address("akka.tcp", "sys", "a", 2552), 1);
        static readonly UniqueAddress nodeB = new UniqueAddress(new Address("akka.tcp", "sys", "b", 2552), 2);
        static readonly UniqueAddress nodeC = new UniqueAddress(new Address("akka.tcp", "sys", "c", 2552), 3);
        static readonly UniqueAddress nodeD = new UniqueAddress(new Address("akka.tcp", "sys", "d", 2552), 4);
        static readonly UniqueAddress nodeE = new UniqueAddress(new Address("akka.tcp", "sys", "e", 2552), 5);

        [TestMethod]
        public void ReachabilityTableMustBeReachableWhenEmpty()
        {
            var r = Reachability.Empty;
            Assert.IsTrue(r.IsReachable(nodeA));
            CollectionAssert.AreEqual(ImmutableHashSet.Create<UniqueAddress>(), r.AllUnreachable);
        }

        [TestMethod]
        public void ReachabilityTableMustBeUnreachableWhenOneObservedUnreachable()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA);
            Assert.IsFalse(r.IsReachable(nodeA));
            CollectionAssert.AreEqual(ImmutableHashSet.Create(nodeA), r.AllUnreachable);
        }

        [TestMethod]
        public void ReachabilityTableMustNotBeReachableWhenTerminated()
        {
            var r = Reachability.Empty.Terminated(nodeB, nodeA);
            Assert.IsFalse(r.IsReachable(nodeA));
            // allUnreachable doesn't include terminated
            CollectionAssert.AreEqual(ImmutableHashSet.Create<UniqueAddress>(), r.AllUnreachable);
            CollectionAssert.AreEqual(ImmutableHashSet.Create(nodeA), r.AllUnreachableOrTerminated);
        }

        [TestMethod]
        public void ReachabilityTableMustNotChangeTerminatedEntry()
        {
            var r = Reachability.Empty.Terminated(nodeB, nodeA);
            Assert.IsTrue(r == r.Reachable(nodeB, nodeA));
            Assert.IsTrue(r == r.Unreachable(nodeB, nodeA));
        }

        [TestMethod]
        public void ReachabilityTableMustNotChangeWhenSameStatus()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA);
            Assert.IsTrue(r == r.Unreachable(nodeB, nodeA));
        }

        [TestMethod]
        public void ReachabilityTableMustBeUnreachableWhenSomeObservedUnreachableAndOtherReachable()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeA).Reachable(nodeD, nodeA);
            Assert.IsFalse(r.IsReachable(nodeA));
        }

        [TestMethod]
        public void ReachabilityTableMustReachableWhenAllObservedReachableAgain()
        {
            var r = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeA).
                Reachable(nodeB, nodeA).Reachable(nodeC, nodeA).
                Unreachable(nodeB, nodeC).Unreachable(nodeC, nodeB);
            Assert.IsTrue(r.IsReachable(nodeA));
        }

        [TestMethod]
        public void ReachabilityTableMustBePrunedWhenAllRecordsOfAnObserverAreReachable()
        {
            var r = Reachability.Empty.
                Unreachable(nodeB, nodeA).Unreachable(nodeB, nodeC).
                Unreachable(nodeD, nodeC).
                Reachable(nodeB, nodeA).Reachable(nodeB, nodeC);
            Assert.IsTrue(r.IsReachable(nodeA));
            Assert.IsFalse(r.IsReachable(nodeC));
            var expected1 = ImmutableList.Create(new Reachability.Record(
                nodeD,
                nodeC,
                Reachability.ReachabilityStatus.Unreachable,
                1));
            CollectionAssert.AreEqual(expected1, r.Records);

            var r2 = r.Unreachable(nodeB, nodeD).Unreachable(nodeB, nodeE);
            var expected2 = ImmutableHashSet.Create(
                new Reachability.Record(nodeD, nodeC, Reachability.ReachabilityStatus.Unreachable, 1),
                new Reachability.Record(nodeB, nodeD, Reachability.ReachabilityStatus.Unreachable, 5),
                new Reachability.Record(nodeB, nodeE, Reachability.ReachabilityStatus.Unreachable, 6)
                );
            CollectionAssert.AreEqual(expected2, r2.Records.ToImmutableHashSet());
        }

        [TestMethod]
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
            Assert.AreEqual(Reachability.ReachabilityStatus.Reachable, r.Status(nodeA));
            Assert.AreEqual(Reachability.ReachabilityStatus.Terminated, r.Status(nodeB));
            Assert.AreEqual(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeD));
        }

        [TestMethod]
        public void ReachabilityTableMustHaveCorrectStatusForAMixOfNodes()
        {
            var r = Reachability.Empty.
                Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeA).Unreachable(nodeD, nodeA).
                Unreachable(nodeC, nodeB).Reachable(nodeC, nodeB).Unreachable(nodeD, nodeB).
                Unreachable(nodeD, nodeC).Reachable(nodeD, nodeC).
                Reachable(nodeE, nodeD).
                Unreachable(nodeA, nodeE).Terminated(nodeB, nodeE);

            Assert.AreEqual(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeB, nodeA));
            Assert.AreEqual(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeC, nodeA));
            Assert.AreEqual(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeD, nodeA));

            Assert.AreEqual(Reachability.ReachabilityStatus.Reachable, r.Status(nodeC, nodeB));
            Assert.AreEqual(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeD, nodeB));

            Assert.AreEqual(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeA, nodeE));
            Assert.AreEqual(Reachability.ReachabilityStatus.Terminated, r.Status(nodeB, nodeE));

            Assert.IsFalse(r.IsReachable(nodeA));
            Assert.IsFalse(r.IsReachable(nodeB));
            Assert.IsTrue(r.IsReachable(nodeC));
            Assert.IsTrue(r.IsReachable(nodeD));
            Assert.IsFalse(r.IsReachable(nodeE));

            CollectionAssert.AreEqual(ImmutableHashSet.Create(nodeA, nodeB), r.AllUnreachable);
            CollectionAssert.AreEqual(ImmutableHashSet.Create(nodeE), r.AllUnreachableFrom(nodeA));
            CollectionAssert.AreEqual(ImmutableHashSet.Create(nodeA), r.AllUnreachableFrom(nodeB));
            CollectionAssert.AreEqual(ImmutableHashSet.Create(nodeA), r.AllUnreachableFrom(nodeC));
            CollectionAssert.AreEqual(ImmutableHashSet.Create(nodeA, nodeB), r.AllUnreachableFrom(nodeD));

            var expected = new Dictionary<UniqueAddress, ImmutableHashSet<UniqueAddress>>
            {
                {nodeA, ImmutableHashSet.Create(nodeB, nodeC, nodeD)},
                {nodeB, ImmutableHashSet.Create(nodeD)},
                {nodeE, ImmutableHashSet.Create(nodeA)}
            }.ToImmutableDictionary();

            CollectionAssert.AreEqual(expected, r.ObserversGroupedByUnreachable, new DictionaryOfHashsetComparer());
        }

        class DictionaryOfHashsetComparer : IComparer
        {
            public int Compare(object x, object y)
            {
                var a = (KeyValuePair<UniqueAddress, ImmutableHashSet<UniqueAddress>>)(x);
                var b = (KeyValuePair<UniqueAddress, ImmutableHashSet<UniqueAddress>>)(y);

                if (!a.Key.Equals(b.Key)) return -1;

                if (!a.Value.SequenceEqual(b.Value)) return -1;
                return 0;
            }
        }

        [TestMethod]
        public void ReachabilityTableMustMergeByTakingAllowedSetIntoAccount()
        {
            var r1 = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeD);
            var r2 = r1.Reachable(nodeB, nodeA).Unreachable(nodeD, nodeE).Unreachable(nodeC, nodeA);
            // nodeD not in allowed set
            var allowed = ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeE);
            var merged = r1.Merge(allowed, r2);

            Assert.AreEqual(Reachability.ReachabilityStatus.Reachable, merged.Status(nodeB, nodeA));
            Assert.AreEqual(Reachability.ReachabilityStatus.Unreachable, merged.Status(nodeC, nodeA));
            Assert.AreEqual(Reachability.ReachabilityStatus.Reachable, merged.Status(nodeC, nodeD));
            Assert.AreEqual(Reachability.ReachabilityStatus.Reachable, merged.Status(nodeD, nodeE));
            Assert.AreEqual(Reachability.ReachabilityStatus.Reachable, merged.Status(nodeE, nodeA));

            Assert.IsFalse(merged.IsReachable(nodeA));
            Assert.IsTrue(merged.IsReachable(nodeD));
            Assert.IsTrue(merged.IsReachable(nodeE));

            CollectionAssert.AreEqual(ImmutableHashSet.Create(nodeB, nodeC), merged.Versions.Keys.ToImmutableHashSet());

            var merged2 = r2.Merge(allowed, r1);
            CollectionAssert.AreEqual(merged.Records.ToImmutableHashSet(), merged2.Records.ToImmutableHashSet());
            CollectionAssert.AreEqual(merged.Versions, merged2.Versions);
        }

        [TestMethod]
        public void ReachabilityTableMustMergeCorrectlyAfterPruning()
        {
            var r1 = Reachability.Empty.Unreachable(nodeB, nodeA).Unreachable(nodeC, nodeD);
            var r2 = r1.Unreachable(nodeA, nodeE);
            var r3 = r1.Reachable(nodeB, nodeA); //nodeB pruned
            var merged = r2.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r3);

            CollectionAssert.AreEqual(ImmutableHashSet.Create(
                new Reachability.Record(nodeA, nodeE, Reachability.ReachabilityStatus.Unreachable, 1),
                new Reachability.Record(nodeC, nodeD, Reachability.ReachabilityStatus.Unreachable, 1))
                , merged.Records.ToImmutableHashSet());

            var merged3 = r3.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r2);
            CollectionAssert.AreEqual(merged.Records.ToImmutableHashSet(), merged3.Records.ToImmutableHashSet());
        }

        [TestMethod]
        public void ReachabilityTableMustMergeVersionsCorrectly()
        {
            var r1 = new Reachability(ImmutableList.Create<Reachability.Record>(),
                new Dictionary<UniqueAddress, long> {{nodeA, 3}, {nodeB, 5}, {nodeC, 7}}.ToImmutableDictionary());
            var r2 = new Reachability(ImmutableList.Create<Reachability.Record>(),
                new Dictionary<UniqueAddress, long> { { nodeA, 6 }, { nodeB, 2 }, { nodeD, 1 } }.ToImmutableDictionary());
            var merged = r1.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r2);

            var expected = new Dictionary<UniqueAddress, long> { { nodeA, 6 }, { nodeB, 5 }, { nodeC, 7 }, { nodeD, 1} }.ToImmutableDictionary();
            CollectionAssert.AreEqual(expected, merged.Versions);

            var merged2 = r2.Merge(ImmutableHashSet.Create(nodeA, nodeB, nodeC, nodeD, nodeE), r1);
            CollectionAssert.AreEqual(expected, merged2.Versions);
        }

        [TestMethod]
        public void ReachabilityTableMustRemoveNode()
        {
            var r = Reachability.Empty.
                Unreachable(nodeB, nodeA).
                Unreachable(nodeC, nodeD).
                Unreachable(nodeB, nodeC).
                Unreachable(nodeB, nodeE).
                Remove(ImmutableHashSet.Create(nodeA, nodeB));

            Assert.AreEqual(Reachability.ReachabilityStatus.Reachable, r.Status(nodeB, nodeA));
            Assert.AreEqual(Reachability.ReachabilityStatus.Unreachable, r.Status(nodeC, nodeD));
            Assert.AreEqual(Reachability.ReachabilityStatus.Reachable, r.Status(nodeB, nodeC));
            Assert.AreEqual(Reachability.ReachabilityStatus.Reachable, r.Status(nodeB, nodeE));
        }
    }
}
