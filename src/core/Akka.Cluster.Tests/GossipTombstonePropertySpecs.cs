//-----------------------------------------------------------------------
// <copyright file="GossipTombstonePropertySpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using CsCheck;
using FluentAssertions;
using Xunit;
using static Akka.Cluster.Tests.GossipTombstoneGenerators;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// Property-based specs for gossip removal tombstones.
    ///
    /// The example specs in <c>GossipSpec</c> pin the cases a human thought of. These sample the same
    /// code over a few hundred random gossips per property and check laws instead of outcomes:
    /// merge is commutative and associative, a tombstone always beats a stale member, and a removal
    /// never comes back over a sequence of exchanges.
    ///
    /// Replaying a failure: CsCheck prints the failing seed, e.g.
    ///     Set seed: "0N0000000000" or -e CsCheck_Seed=0N0000000000 to reproduce
    /// Paste it into the failing Sample call as <c>seed: "0N0000000000"</c>, or set the
    /// <c>CsCheck_Seed</c> environment variable and rerun. Iteration counts are fixed so the suite has a
    /// predictable runtime; raise <c>iter</c> locally when hunting something rare.
    /// </summary>
    public class GossipTombstonePropertySpecs
    {
        // Fixed iteration counts, sized so the whole class runs in a few seconds.
        private const int MergeIterations = 2000;
        private const int SequenceIterations = 800;

        private static readonly ImmutableHashSet<MemberStatus> Terminal =
            ImmutableHashSet.Create(MemberStatus.Down, MemberStatus.Exiting);

        // -----------------------------------------------------------------------------------------
        // P1 - commutativity
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P1: Merge is commutative over members, tombstones, reachability and the clock")]
        public void P1_Merge_is_commutative()
        {
            Sides(2).Sample(g =>
            {
                var ab = g[0].Merge(g[1]);
                var ba = g[1].Merge(g[0]);

                // Describe covers members with their status, tombstone keys AND timestamps, reachability
                // records and versions, and the merged vector clock.
                Describe(ab).Should().Be(Describe(ba));
            }, iter: MergeIterations, print: Print);
        }

        // -----------------------------------------------------------------------------------------
        // P2 - idempotence
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P2: Merge with itself changes nothing but the seen table and stale clock entries")]
        public void P2_Merge_is_idempotent()
        {
            Sides(2).Sample(g =>
            {
                var a = g[0];
                var merged = a.Merge(a);

                // Two documented differences, both by design:
                //
                // 1. Merge clears the seen table - "nobody can have seen this new gossip yet" - so the
                //    comparison is against a with its seen table cleared.
                // 2. Merge drops the clock entry of every tombstoned node. A gossip can legitimately hold
                //    a tombstone and a stale clock entry for the same node: MergeTombstones adopts a
                //    tombstone without touching the clock. Merge is where that gets cleaned up, so the
                //    comparison prunes those entries from a as well.
                var expected = a.ClearSeen();
                foreach (var node in TombstonedClockNodes(a))
                    expected = expected.Prune(node);

                Describe(merged).Should().Be(Describe(expected));

                // and the cleaned-up form really is a fixed point
                Describe(merged.Merge(merged)).Should().Be(Describe(merged));
            }, iter: MergeIterations, print: Print);
        }

        // -----------------------------------------------------------------------------------------
        // P3 - associativity
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P3: Merge is associative")]
        public void P3_Merge_is_associative()
        {
            // Shape.NonTerminal keeps Down and Exiting out of the member statuses. The one-sided drop for
            // those two is not associative on its own and predates tombstones - see the comment on
            // GossipTombstoneGenerators.NonTerminalStatuses. Removals still get covered here, by the
            // tombstones, which is what this suite is about.
            Sides(3, Shape.NonTerminal).Sample(g =>
            {
                var left = g[0].Merge(g[1]).Merge(g[2]);
                var right = g[0].Merge(g[1].Merge(g[2]));

                Describe(left).Should().Be(Describe(right));
            }, iter: MergeIterations, print: Print);
        }

        // -----------------------------------------------------------------------------------------
        // P4 - a tombstone beats a member on the other side
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P4: a node tombstoned on either side is gone from the merged members")]
        public void P4_Tombstone_dominates_a_stale_member()
        {
            var covered = 0;

            Sides(2).Sample(g =>
            {
                var merged = g[0].Merge(g[1]);
                var mergedAddresses = merged.Members.Select(m => m.UniqueAddress).ToImmutableHashSet();

                for (var side = 0; side < 2; side++)
                {
                    var other = g[1 - side];
                    foreach (var tombstoned in g[side].Tombstones.Keys)
                    {
                        // whatever status the other side holds it at - Up, WeaklyUp, Joining, anything
                        mergedAddresses.Should().NotContain(tombstoned,
                            "a tombstone is positive evidence of a removal");

                        // and the removed node is gone from the reachability table too, which the
                        // merge gets by filtering reachability through the merged member set
                        merged.Overview.Reachability.AllObservers.Should().NotContain(tombstoned);
                        merged.Overview.Reachability.Versions.Keys.Should().NotContain(tombstoned);

                        if (other.HasMember(tombstoned)) Interlocked.Increment(ref covered);
                    }
                }
            }, iter: MergeIterations, print: Print);

            // the property is worthless if the generator never produced the case it is about
            covered.Should().BeGreaterThan(MergeIterations / 10,
                "the generator must actually produce nodes tombstoned on one side and held as a member on the other");
        }

        // -----------------------------------------------------------------------------------------
        // P5 - a one-sided member with no tombstone is kept
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P5: a one-sided member with no tombstone and a live status is kept")]
        public void P5_One_sided_live_member_is_retained()
        {
            var covered = 0;

            Sides(2).Sample(g =>
            {
                var merged = g[0].Merge(g[1]);
                var mergedAddresses = merged.Members.Select(m => m.UniqueAddress).ToImmutableHashSet();

                for (var side = 0; side < 2; side++)
                {
                    var other = g[1 - side];
                    foreach (var m in g[side].Members)
                    {
                        if (other.HasMember(m.UniqueAddress)) continue;
                        if (Terminal.Contains(m.Status)) continue;
                        if (g[0].Tombstones.ContainsKey(m.UniqueAddress)) continue;
                        if (g[1].Tombstones.ContainsKey(m.UniqueAddress)) continue;

                        // With no evidence of a removal, a one-sided member may simply be a node the other
                        // side has not heard about yet. Dropping it would strand a live process.
                        mergedAddresses.Should().Contain(m.UniqueAddress);
                        Interlocked.Increment(ref covered);
                    }
                }
            }, iter: MergeIterations, print: Print);

            covered.Should().BeGreaterThan(MergeIterations / 10,
                "the generator must actually produce one-sided live members with no tombstone");
        }

        // -----------------------------------------------------------------------------------------
        // P6 - a one-sided Down or Exiting member is still dropped
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P6: a one-sided Down or Exiting member is dropped with no tombstone present")]
        public void P6_One_sided_terminal_member_is_dropped()
        {
            var covered = 0;

            Sides(2).Sample(g =>
            {
                var merged = g[0].Merge(g[1]);
                var mergedAddresses = merged.Members.Select(m => m.UniqueAddress).ToImmutableHashSet();

                for (var side = 0; side < 2; side++)
                {
                    var other = g[1 - side];
                    foreach (var m in g[side].Members)
                    {
                        if (other.HasMember(m.UniqueAddress)) continue;
                        if (!Terminal.Contains(m.Status)) continue;

                        // the tombstone check widened the drop condition with an OR - it did not replace it
                        mergedAddresses.Should().NotContain(m.UniqueAddress);
                        Interlocked.Increment(ref covered);
                    }
                }
            }, iter: MergeIterations, print: Print);

            covered.Should().BeGreaterThan(MergeIterations / 10,
                "the generator must actually produce one-sided Down and Exiting members");
        }

        // -----------------------------------------------------------------------------------------
        // P7 - members and tombstones never intersect
        //
        // Dominance is not the same on both gossip-reception paths, so this is four properties:
        //
        //   P7a  Merge (the Concurrent branch): the tombstone wins.
        //   P7b  Merge, with a side that breaks the invariant by holding a node as a member AND as a
        //        tombstone: the tombstone still wins.
        //   P7c  MergeTombstones (the Same/Before/After branches): the winner's member wins, because the
        //        implementation refuses to adopt a tombstone for a node it still holds. Both sides of the
        //        pair stay disjoint either way.
        //   P7d  the one input Merge cannot make disjoint - and why the Gossip constructor forbids it.
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P7a: merged members and merged tombstones never intersect")]
        public void P7a_Merged_members_and_tombstones_are_disjoint()
        {
            Sides(2).Sample(g =>
            {
                foreach (var merged in new[] { g[0].Merge(g[1]), g[1].Merge(g[0]) })
                {
                    var addresses = merged.Members.Select(m => m.UniqueAddress).ToImmutableHashSet();
                    addresses.Intersect(merged.Tombstones.Keys).Should().BeEmpty();
                }
            }, iter: MergeIterations, print: Print);
        }

        [Fact(DisplayName = "P7b: a side that holds a node as a member and as a tombstone still loses the member")]
        public void P7b_Merge_survives_a_side_that_breaks_the_invariant()
        {
            var covered = 0;

            // Shape.Adversarial lets a side carry the same node as a member and as a tombstone. The Gossip
            // constructor rejects that under AKKA_CLUSTER_ASSERT=on; nothing rejects it otherwise, so the
            // merge is checked against it here.
            Sides(2, Shape.Adversarial).Sample(g =>
            {
                for (var side = 0; side < 2; side++)
                {
                    var other = g[1 - side];
                    foreach (var key in g[side].Tombstones.Keys)
                    {
                        // this side holds the node as a member and as a tombstone, and the other side
                        // does not hold it at all. The case both sides hold it is P7d.
                        if (!g[side].HasMember(key) || other.HasMember(key)) continue;

                        foreach (var merged in new[] { g[0].Merge(g[1]), g[1].Merge(g[0]) })
                        {
                            merged.Members.Select(m => m.UniqueAddress).Should().NotContain(key);
                            merged.Tombstones.Keys.Should().Contain(key);
                        }

                        Interlocked.Increment(ref covered);
                    }
                }
            }, iter: MergeIterations, print: Print);

            covered.Should().BeGreaterThan(MergeIterations / 10,
                "Shape.Adversarial must actually produce a side holding a node as member and tombstone");
        }

        [Fact(DisplayName = "P7c: MergeTombstones refuses a tombstone for a node the winner still holds")]
        public void P7c_MergeTombstones_keeps_the_winner_disjoint()
        {
            var covered = 0;

            Sides(2).Sample(g =>
            {
                foreach (var (winner, loser) in new[] { (g[0], g[1]), (g[1], g[0]) })
                {
                    var merged = winner.MergeTombstones(loser);

                    // the winner's members are untouched, so disjointness is what the guard buys
                    merged.Members.Select(m => m.UniqueAddress).ToImmutableHashSet()
                        .Intersect(merged.Tombstones.Keys).Should().BeEmpty();
                    merged.Members.SetEquals(winner.Members).Should().BeTrue();

                    foreach (var kv in loser.Tombstones)
                    {
                        if (winner.HasMember(kv.Key))
                        {
                            // dropped on purpose. Every tombstone write bumps the clock, so a gossip that
                            // wins the causal comparison is never behind on removals and this branch is a
                            // guard rather than a decision.
                            merged.Tombstones.Keys.Should().NotContain(kv.Key);
                            Interlocked.Increment(ref covered);
                        }
                        else
                        {
                            merged.Tombstones.Keys.Should().Contain(kv.Key);
                        }
                    }
                }
            }, iter: MergeIterations, print: Print);

            covered.Should().BeGreaterThan(MergeIterations / 10,
                "the generator must actually produce a loser tombstone for a node the winner holds");
        }

        [Fact(DisplayName = "P7d: Merge cannot drop a member both sides hold, even when one side tombstones it")]
        public void P7d_Merge_cannot_decide_a_member_both_sides_hold()
        {
            var covered = 0;

            // Pins the precondition the Gossip constructor enforces. PickHighestPriority consults
            // tombstones only on its one-sided branch; a node both sides hold as a member is picked by
            // status alone. So a gossip that carries a member and a tombstone for the same node, merged
            // with a gossip that holds that member, comes out non-disjoint. That is exactly the state
            // AssertInvariants rejects, and why it rejects it.
            Sides(2, Shape.Adversarial).Sample(g =>
            {
                for (var side = 0; side < 2; side++)
                {
                    var other = g[1 - side];
                    foreach (var key in g[side].Tombstones.Keys)
                    {
                        if (!g[side].HasMember(key) || !other.HasMember(key)) continue;

                        var merged = g[0].Merge(g[1]);
                        merged.Members.Select(m => m.UniqueAddress).Should().Contain(key);
                        merged.Tombstones.Keys.Should().Contain(key);
                        Interlocked.Increment(ref covered);
                    }
                }
            }, iter: MergeIterations, print: Print);

            covered.Should().BeGreaterThan(0,
                "the generator must actually produce a node held as a member on both sides and tombstoned on one");
        }

        // -----------------------------------------------------------------------------------------
        // P8 - clock hygiene
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P8: no tombstoned node keeps a vector clock entry through a merge")]
        public void P8_Merge_prunes_the_clock_of_tombstoned_nodes()
        {
            var covered = 0;

            Sides(2).Sample(g =>
            {
                foreach (var merged in new[] { g[0].Merge(g[1]), g[1].Merge(g[0]) })
                {
                    foreach (var key in merged.Tombstones.Keys)
                    {
                        // Merging clocks resurrects an entry the same way merging members resurrects a
                        // member, so filtering members alone would leave the removed node in the clock
                        // forever.
                        merged.Version.Versions.Keys.Should().NotContain(VclockNodeOf(key));
                    }
                }

                // count the iterations where a side actually carried a clock entry for a tombstoned node
                for (var side = 0; side < 2; side++)
                {
                    if (g[side].Tombstones.Keys.Any(k =>
                            g[0].Version.Versions.ContainsKey(VclockNodeOf(k)) ||
                            g[1].Version.Versions.ContainsKey(VclockNodeOf(k))))
                    {
                        Interlocked.Increment(ref covered);
                        break;
                    }
                }
            }, iter: MergeIterations, print: Print);

            covered.Should().BeGreaterThan(MergeIterations / 10,
                "the generator must actually produce clock entries for tombstoned nodes");
        }

        // -----------------------------------------------------------------------------------------
        // P9 - the union law
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P9: merged tombstones are the union, and a collision resolves to the later timestamp")]
        public void P9_Tombstones_union_with_the_max_timestamp()
        {
            var collisions = 0;

            Sides(2).Sample(g =>
            {
                var expectedKeys = g[0].Tombstones.Keys.Union(g[1].Tombstones.Keys).ToImmutableHashSet();

                foreach (var merged in new[] { g[0].Merge(g[1]), g[1].Merge(g[0]) })
                {
                    merged.Tombstones.Keys.ToImmutableHashSet().SetEquals(expectedKeys).Should().BeTrue();

                    foreach (var key in expectedKeys)
                    {
                        var expected = Math.Max(
                            g[0].Tombstones.TryGetValue(key, out var t0) ? t0 : long.MinValue,
                            g[1].Tombstones.TryGetValue(key, out var t1) ? t1 : long.MinValue);
                        merged.Tombstones[key].Should().Be(expected);
                    }
                }

                // the winner-picked branches take the same union, minus the nodes the winner still holds
                foreach (var (winner, loser) in new[] { (g[0], g[1]), (g[1], g[0]) })
                {
                    var merged = winner.MergeTombstones(loser);
                    var expectedUnion = winner.Tombstones.Keys
                        .Union(loser.Tombstones.Keys.Where(k => !winner.HasMember(k)))
                        .ToImmutableHashSet();

                    merged.Tombstones.Keys.ToImmutableHashSet().SetEquals(expectedUnion).Should().BeTrue();

                    foreach (var key in expectedUnion)
                    {
                        var expected = Math.Max(
                            winner.Tombstones.TryGetValue(key, out var w) ? w : long.MinValue,
                            loser.Tombstones.TryGetValue(key, out var l) && !winner.HasMember(key)
                                ? l
                                : long.MinValue);
                        merged.Tombstones[key].Should().Be(expected);
                    }
                }

                foreach (var key in g[0].Tombstones.Keys)
                    if (g[1].Tombstones.ContainsKey(key))
                    {
                        Interlocked.Increment(ref collisions);
                        break;
                    }
            }, iter: MergeIterations, print: Print);

            collisions.Should().BeGreaterThan(MergeIterations / 10,
                "the timestamp pool must be small enough that both sides tombstone the same node");
        }

        // -----------------------------------------------------------------------------------------
        // P10 - prune laws
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P10: PruneTombstones drops entries at or before the cutoff and is a no-op otherwise")]
        public void P10_Prune_drops_expired_entries_only()
        {
            // Timestamps come out of [1, 4], so cutoffs from 0 to 5 cover both boundaries and everything
            // between. removeEarlierThan is inclusive: the implementation drops entries at or before it.
            Gen.Select(Sides(1), Gen.Long[0L, 5L], (g, cutoff) => (Gossip: g[0], Cutoff: cutoff))
                .Sample(t =>
                {
                    var pruned = t.Gossip.PruneTombstones(t.Cutoff);

                    var expired = t.Gossip.Tombstones.Where(kv => kv.Value <= t.Cutoff).ToList();
                    var kept = t.Gossip.Tombstones.Where(kv => kv.Value > t.Cutoff).ToList();

                    if (expired.Count == 0)
                    {
                        // The leader's convergence tick publishes only when this reference changes. Value
                        // equality is not enough: a fresh instance would bump the vector clock and reset
                        // the seen table on every quiet tick, and the cluster would never settle.
                        pruned.Should().BeSameAs(t.Gossip);
                    }
                    else
                    {
                        pruned.Tombstones.Keys.ToImmutableHashSet()
                            .SetEquals(kept.Select(kv => kv.Key)).Should().BeTrue();
                        foreach (var kv in kept)
                            pruned.Tombstones[kv.Key].Should().Be(kv.Value);
                    }

                    // pruning never touches anything else
                    pruned.Members.SetEquals(t.Gossip.Members).Should().BeTrue();
                    pruned.Version.Versions.Should().Equal(t.Gossip.Version.Versions);
                    pruned.Overview.Seen.SetEquals(t.Gossip.Overview.Seen).Should().BeTrue();
                }, iter: MergeIterations, print: t => Describe(t.Gossip) + $"\ncutoff={t.Cutoff}");
        }

        // -----------------------------------------------------------------------------------------
        // P11 - the write path
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P11: RemoveAll writes a tombstone for every removed node and strips it everywhere else")]
        public void P11_RemoveAll_writes_a_tombstone_for_every_removal()
        {
            // the removal set is drawn as a bitmask over the universe; the timestamp is supplied, never read
            // off the clock
            Gen.Select(Sides(1), Gen.Int[0, (1 << NodeCount) - 1], Gen.Long[100L, 200L],
                    (g, mask, ts) => (Gossip: g[0], Mask: mask, Timestamp: ts))
                .Sample(t =>
                {
                    var nodes = Enumerable.Range(0, NodeCount)
                        .Where(i => (t.Mask & (1 << i)) != 0)
                        .Select(Node)
                        .ToImmutableHashSet();

                    var removed = t.Gossip.RemoveAll(nodes, t.Timestamp);

                    if (nodes.Count == 0)
                    {
                        removed.Should().BeSameAs(t.Gossip);
                        return;
                    }

                    foreach (var node in nodes)
                    {
                        removed.Tombstones.Should().ContainKey(node);
                        removed.Tombstones[node].Should().Be(t.Timestamp);
                        removed.HasMember(node).Should().BeFalse();
                        removed.Overview.Seen.Should().NotContain(node);
                        removed.Overview.Reachability.AllObservers.Should().NotContain(node);
                        removed.Overview.Reachability.AllUnreachable.Should().NotContain(node);
                        removed.Overview.Reachability.Versions.Keys.Should().NotContain(node);
                        removed.Version.Versions.Keys.Should().NotContain(VclockNodeOf(node));
                    }

                    // everything else is left alone
                    foreach (var m in t.Gossip.Members.Where(m => !nodes.Contains(m.UniqueAddress)))
                        removed.Members.Should().Contain(m);

                    foreach (var kv in t.Gossip.Tombstones.Where(kv => !nodes.Contains(kv.Key)))
                        removed.Tombstones[kv.Key].Should().Be(kv.Value);

                    foreach (var kv in t.Gossip.Version.Versions.Where(kv =>
                                 !nodes.Select(VclockNodeOf).Contains(kv.Key)))
                        removed.Version.Versions[kv.Key].Should().Be(kv.Value);
                }, iter: MergeIterations, print: t => Describe(t.Gossip) + $"\nmask={t.Mask} ts={t.Timestamp}");
        }

        // -----------------------------------------------------------------------------------------
        // P13 / P14 - sequences of removals and exchanges
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P13: a removal never comes back over a random sequence of gossip exchanges")]
        public void P13_Removals_do_not_resurrect_over_a_history()
        {
            var removals = 0;

            Histories().Sample(h =>
            {
                var removed = RunHistory(h, checkMonotonicity: false, checkConvergence: true);
                if (removed > 0) Interlocked.Increment(ref removals);
            }, iter: SequenceIterations, print: h => h.ToString());

            removals.Should().BeGreaterThan(SequenceIterations / 2,
                "most histories must actually remove something");
        }

        [Fact(DisplayName = "P14: a node's tombstone set never shrinks except across a prune")]
        public void P14_Tombstone_sets_only_grow()
        {
            var removals = 0;

            Histories().Sample(h =>
            {
                var removed = RunHistory(h, checkMonotonicity: true, checkConvergence: false);
                if (removed > 0) Interlocked.Increment(ref removals);
            }, iter: SequenceIterations, print: h => h.ToString());

            removals.Should().BeGreaterThan(SequenceIterations / 2,
                "most histories must actually remove something");
        }

        /// <summary>
        /// A random op sequence over N virtual nodes, each holding a gossip over the same initial
        /// membership. Ops are ints, decoded by <see cref="RunHistory"/>.
        /// </summary>
        private static Gen<History> Histories() =>
            Gen.Select(Gen.Int[3, 5], Gen.Int[0, 899].Array[10, 30], (n, ops) => new History(n, ops));

        private sealed class History
        {
            public History(int nodes, int[] ops)
            {
                Nodes = nodes;
                Ops = ops;
            }

            public int Nodes { get; }
            public int[] Ops { get; }

            public override string ToString() => $"nodes={Nodes} ops=[{string.Join(",", Ops)}]";
        }

        /// <summary>
        /// Replays a history against N gossips and an oracle - a plain set of removed UIDs. Returns how
        /// many nodes the oracle saw removed, so the caller can check the histories were not all no-ops.
        ///
        /// Exchanges use the same branch selection <c>ClusterCoreDaemon.ReceiveGossip</c> uses, so the
        /// property covers the paths a real cluster takes, not just <see cref="Gossip.Merge"/>.
        /// Timestamps come from a counter passed in here, never from the wall clock.
        ///
        /// The N virtual nodes stay in the cluster for the whole history: a removal never targets one of
        /// them. A node that keeps gossiping after being removed is not a history the cluster produces -
        /// the removed node shuts down, and <c>ReceiveGossip</c> drops gossip that does not contain the
        /// receiver and gossip from a sender the receiver no longer holds, so a zombie is deaf and
        /// ignored in both directions. Modelling one instead would only measure that omission.
        /// </summary>
        private static int RunHistory(History history, bool checkMonotonicity, bool checkConvergence)
        {
            var n = history.Nodes;

            // every virtual node starts from the same gossip: the whole universe, all Up, one clock tick
            // each so removals have a clock entry to prune
            var initialMembers = Enumerable.Range(0, NodeCount)
                .Select(i => MemberOf(i, MemberStatus.Up))
                .ToImmutableSortedSet();

            var initialVersion = Enumerable.Range(0, NodeCount)
                .Aggregate(VectorClock.Create(), (v, i) => v.Increment(VclockNodeOf(Node(i))));

            var initial = new Gossip(initialMembers, new GossipOverview(), initialVersion);

            var gossips = Enumerable.Repeat(initial, n).ToArray();
            var identities = Enumerable.Range(0, n).Select(Node).ToArray();

            var oracle = new HashSet<UniqueAddress>();
            var timestamp = 1L;

            // a cutoff below every timestamp this history will ever write, so the prune op expires nothing
            const long QuietCutoff = 0L;

            foreach (var raw in history.Ops)
            {
                var before = gossips.Select(g => g.Tombstones.Keys.ToImmutableHashSet()).ToArray();
                var kind = raw % 3;
                var isPrune = kind == 2;

                switch (kind)
                {
                    case 0:
                    {
                        // the leader removes a live member that is not one of the gossiping nodes
                        var actor = raw / 3 % n;
                        var g = gossips[actor];
                        var candidates = g.Members
                            .Where(m => !identities.Contains(m.UniqueAddress))
                            .ToArray();
                        if (candidates.Length == 0) break;

                        var victim = candidates[raw / 30 % candidates.Length].UniqueAddress;
                        var removed = g.RemoveAll(ImmutableHashSet.Create(victim), timestamp++);

                        // ClusterCoreDaemon.UpdateLatestGossip stamps every local change with the node's
                        // own clock entry. Without that bump a removal would be invisible to the causal
                        // comparison below.
                        gossips[actor] = StampLocalChange(removed, identities[actor]);
                        oracle.Add(victim);
                        break;
                    }

                    case 1:
                    {
                        var receiver = raw / 3 % n;
                        var sender = raw / 30 % n;
                        if (receiver == sender) break;
                        gossips[receiver] = Receive(gossips[receiver], gossips[sender], identities[receiver]);
                        break;
                    }

                    default:
                    {
                        var node = raw / 3 % n;
                        var pruned = gossips[node].PruneTombstones(QuietCutoff);

                        // nothing expired, so the leader's no-op guard must hold
                        pruned.Should().BeSameAs(gossips[node]);
                        gossips[node] = pruned;
                        break;
                    }
                }

                // (a) and (b): after every op, no node holds a UID as a member and as a tombstone
                for (var i = 0; i < n; i++)
                {
                    var g = gossips[i];
                    var addresses = g.Members.Select(m => m.UniqueAddress).ToImmutableHashSet();

                    addresses.Intersect(g.Tombstones.Keys).Should().BeEmpty(
                        "node {0} must keep members and tombstones disjoint", i);

                    foreach (var key in g.Tombstones.Keys)
                        g.HasMember(key).Should().BeFalse(
                            "node {0} carries a tombstone for {1}", i, key);

                    // and nothing is invented out of thin air
                    addresses.Except(initialMembers.Select(m => m.UniqueAddress)).Should().BeEmpty();
                }

                if (checkMonotonicity && !isPrune)
                {
                    for (var i = 0; i < n; i++)
                        before[i].Except(gossips[i].Tombstones.Keys).Should().BeEmpty(
                            "node {0} must not lose a tombstone outside a prune", i);
                }
            }

            if (!checkConvergence) return oracle.Count;

            // exchange every ordered pair until nothing moves
            var rounds = 0;
            while (rounds++ < 25)
            {
                var snapshot = gossips.Select(DescribeCore).ToArray();

                for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    gossips[i] = Receive(gossips[i], gossips[j], identities[i]);
                }

                if (gossips.Select(DescribeCore).SequenceEqual(snapshot) &&
                    snapshot.Distinct().Count() == 1)
                    break;
            }

            var converged = gossips.Select(DescribeCore).Distinct().ToArray();
            converged.Length.Should().Be(1,
                "every node must end on the same members, tombstones and clock, got:\n{0}",
                string.Join("\n", converged));

            var expectedMembers = initialMembers
                .Where(m => !oracle.Contains(m.UniqueAddress))
                .Select(m => m.UniqueAddress)
                .ToImmutableHashSet();

            foreach (var g in gossips)
            {
                g.Members.Select(m => m.UniqueAddress).ToImmutableHashSet()
                    .SetEquals(expectedMembers).Should().BeTrue();
                g.Tombstones.Keys.ToImmutableHashSet().SetEquals(oracle).Should().BeTrue();
            }

            return oracle.Count;
        }

        /// <summary>
        /// Mirrors <c>ClusterCoreDaemon.UpdateLatestGossip</c>: stamp the change with the node's own clock
        /// entry, then mark the gossip as seen by nobody but this node.
        /// </summary>
        private static Gossip StampLocalChange(Gossip gossip, UniqueAddress self) =>
            gossip.Increment(VclockNodeOf(self)).OnlySeen(self);

        /// <summary>
        /// Mirrors the four comparison branches of <c>ClusterCoreDaemon.ReceiveGossip</c>, including the
        /// conflicting-gossip clock pruning the concurrent branch does before merging.
        /// </summary>
        private static Gossip Receive(Gossip local, Gossip remote, UniqueAddress self)
        {
            Gossip winner;
            switch (remote.Version.CompareTo(local.Version))
            {
                case VectorClock.Ordering.Same:
                    winner = remote.MergeSeen(local).MergeTombstones(local);
                    break;
                case VectorClock.Ordering.Before:
                    winner = local.MergeTombstones(remote);
                    break;
                case VectorClock.Ordering.After:
                    winner = remote.MergeTombstones(local);
                    break;
                default:
                    winner = PruneConflicting(remote, local).Merge(PruneConflicting(local, remote));
                    break;
            }

            return winner.Seen(self);
        }

        private static Gossip PruneConflicting(Gossip gossip, Gossip other) =>
            gossip.Members.Aggregate(gossip, (g, m) =>
                MembershipState.RemoveUnreachableWithMemberStatus.Contains(m.Status) &&
                !other.Members.Contains(m)
                    ? g.Prune(VclockNodeOf(m.UniqueAddress))
                    : g);
    }
}
