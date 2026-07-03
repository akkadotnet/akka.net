//-----------------------------------------------------------------------
// <copyright file="ConsistentHashSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Akka.Routing;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Routing
{
    /// <summary>
    /// Unit tests for <see cref="ConsistentHash{T}"/> ring construction, with a focus on the
    /// 32-bit hash collision handling introduced for
    /// <a href="https://github.com/akkadotnet/akka.net/issues/8031">#8031</a>.
    ///
    /// Prior to the fix a single collision in the 32-bit ring key space made
    /// <see cref="ConsistentHash.Create{T}"/> throw, which wedged the entire consistent-hashing
    /// router (every message returned <c>NoRoutee</c>) until a manual restart. The ring now
    /// linear-probes to the next free slot instead of throwing.
    /// </summary>
    public class ConsistentHashSpec
    {
        // A genuine 32-bit collision discovered by brute force: at virtual-nodes-factor 10 the
        // virtual nodes of "2842" and "7681" collide (specifically "2842" vnode 10 and "7681"
        // vnode 6 both hash to 368115337). The set has two colliding vnodes out of twenty.
        // The HasVnodeCollision guard below re-proves this against the real hash on every run.
        private const string CollisionA = "2842";
        private const string CollisionB = "7681";
        private const int CollisionFactor = 10;

        /// <summary>
        /// A reference type identified only by <see cref="ToString"/> (no <c>Equals</c> override),
        /// matching how <see cref="ConsistentHash{T}"/> is documented to identify nodes. Used to prove
        /// ring identity is ToString-based, not reference/Default-equality based.
        /// </summary>
        private sealed class NamedNode
        {
            private readonly string _name;
            public NamedNode(string name) => _name = name;
            public override string ToString() => _name;
        }

        #region Helpers

        /// <summary>
        /// Reads the private ring dictionary out of a <see cref="ConsistentHash{T}"/> so tests can
        /// assert on the exact key-&gt;node mapping (there is no public accessor).
        /// </summary>
        private static SortedDictionary<int, T> Ring<T>(ConsistentHash<T> hash)
        {
            var field = typeof(ConsistentHash<T>).GetField("_nodes", BindingFlags.NonPublic | BindingFlags.Instance);
            field.Should().NotBeNull("the ConsistentHash<T>._nodes field is required by these tests");
            return (SortedDictionary<int, T>)field.GetValue(hash);
        }

        /// <summary>
        /// Faithful reproduction of the pre-#8031 <see cref="ConsistentHash.Create{T}"/>: insert the
        /// natural virtual-node hashes in input order and throw (via <c>SortedDictionary.Add</c>) on a
        /// duplicate key. Used to prove the new algorithm produces a byte-identical ring whenever the
        /// legacy algorithm was able to build one (rolling-upgrade / split-brain safety).
        /// </summary>
        private static SortedDictionary<int, T> LegacyRing<T>(IEnumerable<T> nodes, int factor)
        {
            var dict = new SortedDictionary<int, T>();
            foreach (var node in nodes)
            {
                var nodeHash = ConsistentHash.HashFor(node.ToString());
                for (var v = 1; v <= factor; v++)
                    dict.Add(ConsistentHash.ConcatenateNodeHash(nodeHash, v), node);
            }
            return dict;
        }

        /// <summary>
        /// True if the given nodes produce at least one duplicate virtual-node key in the 32-bit ring.
        /// Uses the real internal hash so the collision fixtures stay pinned to the shipping algorithm.
        /// </summary>
        private static bool HasVnodeCollision(IEnumerable<string> nodes, int factor)
        {
            var seen = new HashSet<int>();
            foreach (var n in nodes)
            {
                var nodeHash = ConsistentHash.HashFor(n);
                for (var v = 1; v <= factor; v++)
                    if (!seen.Add(ConsistentHash.ConcatenateNodeHash(nodeHash, v)))
                        return true;
            }
            return false;
        }

        private static void AssertSameRing<T>(SortedDictionary<int, T> expected, SortedDictionary<int, T> actual)
        {
            actual.Count.Should().Be(expected.Count);
            foreach (var kv in expected)
            {
                actual.TryGetValue(kv.Key, out var value).Should().BeTrue($"key [{kv.Key}] must be present");
                value.Should().Be(kv.Value, $"key [{kv.Key}] must map to the same node");
            }
        }

        #endregion

        [Fact]
        public void Create_must_not_throw_when_two_node_keys_collide_in_the_32bit_ring()
        {
            // Guard: the fixture must actually collide, otherwise this test proves nothing.
            HasVnodeCollision(new[] { CollisionA, CollisionB }, CollisionFactor).Should().BeTrue(
                "the collision fixture must be a real 32-bit collision under the shipping hash");

            Action act = () => ConsistentHash.Create(new[] { CollisionA, CollisionB }, CollisionFactor);
            act.Should().NotThrow("a 32-bit collision must never wedge the ring (#8031)");
        }

        [Fact]
        public void Create_must_keep_every_virtual_node_when_resolving_a_collision()
        {
            var ring = Ring(ConsistentHash.Create(new[] { CollisionA, CollisionB }, CollisionFactor));

            // Re-probe keeps ALL virtual nodes (distribution-neutral); coalescing would drop some.
            ring.Count.Should().Be(2 * CollisionFactor);
            ring.Values.Count(v => v == CollisionA).Should().Be(CollisionFactor);
            ring.Values.Count(v => v == CollisionB).Should().Be(CollisionFactor);
        }

        [Fact]
        public void Create_must_build_an_identical_ring_regardless_of_input_order()
        {
            // Cross-node determinism: every node in the cluster must build the same ring even when a
            // collision is resolved, no matter what order it happens to see the routees in.
            var forward = Ring(ConsistentHash.Create(new[] { CollisionA, CollisionB }, CollisionFactor));
            var reverse = Ring(ConsistentHash.Create(new[] { CollisionB, CollisionA }, CollisionFactor));

            AssertSameRing(forward, reverse);
        }

        [Fact]
        public void NodeFor_must_route_every_key_to_a_real_node_after_a_collision()
        {
            var hash = ConsistentHash.Create(new[] { CollisionA, CollisionB }, CollisionFactor);
            hash.IsEmpty.Should().BeFalse();

            var reached = new HashSet<string>();
            for (var i = 0; i < 2000; i++)
            {
                var node = hash.NodeFor("key-" + i);
                node.Should().Match<string>(n => n == CollisionA || n == CollisionB);
                reached.Add(node);
            }

            reached.Should().BeEquivalentTo(new[] { CollisionA, CollisionB }, "both nodes must remain reachable");
        }

        [Fact]
        public void Operator_plus_must_not_throw_when_the_added_node_collides()
        {
            var hash = ConsistentHash.Create(new[] { CollisionA }, CollisionFactor);

            ConsistentHash<string> combined = null;
            Action act = () => combined = hash + CollisionB;
            act.Should().NotThrow("adding a colliding node must probe rather than throw (#8031)");

            var ring = Ring(combined);
            ring.Count.Should().Be(2 * CollisionFactor);
            ring.Values.Count(v => v == CollisionB).Should().Be(CollisionFactor);
        }

        [Fact]
        public void Operator_plus_must_produce_the_same_ring_as_Create_including_across_a_collision()
        {
            // Create(S) + x == Create(S ∪ {x}), even when x collides with a node already in S.
            // The incremental Add must resolve the collision in the SAME canonical order as Create,
            // not in insertion order, otherwise rings built by different paths would diverge.
            var incremental = ConsistentHash.Create(new[] { CollisionA }, CollisionFactor) + CollisionB;
            var fromScratch = ConsistentHash.Create(new[] { CollisionA, CollisionB }, CollisionFactor);

            AssertSameRing(Ring(fromScratch), Ring(incremental));
        }

        [Fact]
        public void Operator_plus_must_be_idempotent_for_a_node_already_in_the_ring()
        {
            // Re-adding a present node must NOT duplicate its virtual nodes (which would skew the
            // distribution toward it and grow the ring unbounded across repeated adds).
            var baseRing = ConsistentHash.Create(new[] { CollisionA, CollisionB }, CollisionFactor);
            var afterReadd = baseRing + CollisionA;

            AssertSameRing(Ring(baseRing), Ring(afterReadd));
            Ring(afterReadd).Values.Count(v => v == CollisionA).Should().Be(CollisionFactor);
        }

        [Fact]
        public void Operator_minus_must_drop_every_ring_entry_for_the_node_including_probed_slots()
        {
            // "7681" sorts after "2842", so its colliding virtual node is the one relocated to a
            // probed slot. Removing it must leave NO phantom entry behind (the add/remove asymmetry
            // the review caught), and must equal Create of the remaining node set.
            var full = ConsistentHash.Create(new[] { CollisionA, CollisionB }, CollisionFactor);
            var afterRemove = full - CollisionB;

            var ring = Ring(afterRemove);
            ring.Values.Should().NotContain(CollisionB, "all entries for the removed node, probed slots included, must be gone");
            ring.Count.Should().Be(CollisionFactor);
            AssertSameRing(Ring(ConsistentHash.Create(new[] { CollisionA }, CollisionFactor)), ring);
        }

        [Fact]
        public void Create_must_identify_nodes_by_ToString_and_dedupe_duplicates()
        {
            // Same value listed twice must yield a single set of virtual nodes, not a probed-in
            // second set that skews the distribution toward it (#8031).
            var byValue = Ring(ConsistentHash.Create(new[] { CollisionA, CollisionA }, CollisionFactor));
            byValue.Count.Should().Be(CollisionFactor);
            byValue.Values.Should().OnlyContain(v => v == CollisionA);

            // Two distinct instances with the same ToString (no Equals override) are the same node
            // to the ring - identity is ToString-based, not reference-based.
            var ring = Ring(ConsistentHash.Create(new[] { new NamedNode("srv1"), new NamedNode("srv1") }, CollisionFactor));
            ring.Count.Should().Be(CollisionFactor, "nodes are identified by ToString(), not reference identity");
        }

        [Fact]
        public void Operator_plus_must_be_idempotent_by_ToString_for_reference_types()
        {
            var ring = ConsistentHash.Create(new[] { new NamedNode("srv1") }, CollisionFactor);

            // Fresh instance, same ToString: re-adding must be a no-op, not an unbounded duplication.
            var afterReadd = ring + new NamedNode("srv1");

            Ring(afterReadd).Count.Should().Be(CollisionFactor, "re-adding by ToString identity must be a no-op");
        }

        [Fact]
        public void Operator_minus_must_match_by_ToString_not_reference_or_Equals()
        {
            var ring = ConsistentHash.Create(new[] { new NamedNode("a"), new NamedNode("b") }, CollisionFactor);

            // Remove via a DIFFERENT instance whose ToString is "a": must drop a's entries (ToString
            // identity - reference equality would match nothing) and must NOT drop "b".
            var afterRemove = Ring(ring - new NamedNode("a"));

            afterRemove.Count.Should().Be(CollisionFactor);
            afterRemove.Values.Should().OnlyContain(n => n.ToString() == "b");
        }

        [Fact]
        public void The_legacy_algorithm_reproduces_the_original_crash_on_the_collision_fixture()
        {
            // Sanity check that our before/after harness reproduces the exact failure #8031 reported,
            // so the equality proof below is meaningful.
            Action legacy = () => LegacyRing(new[] { CollisionA, CollisionB }, CollisionFactor);
            legacy.Should().Throw<ArgumentException>();
        }

        /// <summary>
        /// Rolling-upgrade / split-brain safety proof: for every routee set the legacy (pre-#8031)
        /// algorithm can build, the new algorithm must build a byte-identical ring. If that holds,
        /// a cluster running a mix of old and new nodes routes every key identically, so upgrading
        /// node-by-node cannot cause a split brain. The only sets where the rings differ are the ones
        /// the legacy algorithm cannot build at all (it throws and the old node is already wedged).
        /// </summary>
        [Fact]
        public void Create_must_produce_the_legacy_ring_whenever_the_legacy_algorithm_succeeds()
        {
            var rnd = new Random(20260701);
            var factors = new[] { 1, 3, 5, 10, 17 };
            var counts = new[] { 2, 5, 25, 100, 500, 2000 };

            var compared = 0;
            var legacyCollisions = 0;

            foreach (var factor in factors)
            foreach (var count in counts)
            for (var trial = 0; trial < 3; trial++)
            {
                var nodes = Enumerable.Range(0, count)
                    .Select(_ => "node-" + rnd.Next())
                    .Distinct()
                    .ToArray();

                SortedDictionary<int, string> legacy;
                try
                {
                    legacy = LegacyRing(nodes, factor);
                }
                catch (ArgumentException)
                {
                    // The legacy builder would have thrown and wedged the router here. The new code
                    // must instead build a usable ring - there is no legacy ring to compare against.
                    legacyCollisions++;
                    Action act = () => ConsistentHash.Create(nodes, factor);
                    act.Should().NotThrow("the new ring must tolerate the collision the legacy code could not");
                    continue;
                }

                var updated = Ring(ConsistentHash.Create(nodes, factor));
                AssertSameRing(legacy, updated);
                compared++;
            }

            compared.Should().BeGreaterThan(0, "the proof must actually compare non-colliding rings");
            // legacyCollisions is informational: at these scales the high-end configs usually exercise
            // the collision branch too, but the guarantee that matters is the equality asserted above.
        }
    }
}
