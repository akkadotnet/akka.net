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
using Akka.Cluster.Configuration;
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
    /// Time is data here, not a clock. Every timestamp a property writes is passed in, and P16 carries a
    /// virtual now that its histories jump forward by drawn hours. Nothing under test reads a clock -
    /// <see cref="Gossip.RemoveAll"/> and <see cref="Gossip.PruneTombstones"/> both take the instant as a
    /// parameter - so sampling sparse instants across a simulated week is sound and needs no TimeProvider.
    /// The two wall-clock reads that remain are in <c>ClusterCoreDaemon.LeaderActionsOnConvergence</c>,
    /// which is what supplies those parameters; that call site is out of this suite's reach by design, and
    /// what it computes is exactly the arithmetic P16 checks.
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
        private const int LineageIterations = 2000;

        /// <summary>
        /// The real retention window, read off the shipped default rather than picked here, so P16 checks
        /// the arithmetic the cluster actually runs.
        /// </summary>
        private static readonly TimeSpan PruneWindow =
            new ClusterSettings(ClusterConfigFactory.Default(), "gossip-tombstone-property-specs")
                .PruneGossipTombstonesAfter;

        /// <summary>
        /// How far apart two nodes' clocks may sit in P16. Node clocks are drawn in [0, MaxSkew], so the
        /// widest disagreement between any minting node and any pruning node is MaxSkew.
        /// </summary>
        private static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);

        private static readonly ImmutableHashSet<MemberStatus> Terminal =
            ImmutableHashSet.Create(MemberStatus.Down, MemberStatus.Exiting);

        /// <summary>
        /// Shape.Adversarial builds a gossip that holds a node as a member and as a tombstone. The Gossip
        /// constructor throws on exactly that under AKKA_CLUSTER_ASSERT=on, so the properties about it
        /// cannot run there - the input they are about cannot be constructed.
        /// </summary>
        private const string AdversarialSkipReason =
            "AKKA_CLUSTER_ASSERT=on rejects the member-and-tombstone gossip this property is about";

        // -----------------------------------------------------------------------------------------
        // P1 - commutativity
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P1: Merge is commutative over members, tombstones, reachability and the clock")]
        public void P1_Merge_is_commutative()
        {
            var sharedObserverAtDifferentVersions = 0;

            Sides(2).Sample(g =>
            {
                var ab = g[0].Merge(g[1]);
                var ba = g[1].Merge(g[0]);

                // Describe covers members with their status, tombstone keys AND timestamps, reachability
                // records and versions, the seen table, and the merged vector clock.
                Describe(ab).Should().Be(Describe(ba));

                // the reachability generator is only worth anything if the two sides really do hold the
                // same observer at different versions - that is what Reachability.Merge arbitrates
                var v0 = g[0].Overview.Reachability.Versions;
                var v1 = g[1].Overview.Reachability.Versions;
                if (v0.Keys.Any(o => v1.TryGetValue(o, out var other) && other != v0[o]))
                    Interlocked.Increment(ref sharedObserverAtDifferentVersions);
            }, iter: MergeIterations, print: Print);

            sharedObserverAtDifferentVersions.Should().BeGreaterThan(MergeIterations / 10,
                "the two sides must share observers at different versions often enough to arbitrate");
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
                var hit = false;

                for (var side = 0; side < 2; side++)
                {
                    var other = g[1 - side];
                    foreach (var tombstoned in g[side].Tombstones.Keys)
                    {
                        // whatever status the other side holds it at - Up, WeaklyUp, Joining, anything
                        mergedAddresses.Should().NotContain(tombstoned,
                            "a tombstone is positive evidence of a removal");

                        // and the removed node is gone from the reachability table too, which the merge
                        // gets by filtering reachability through the merged member set: as an observer,
                        // as a subject, and in the versions table
                        merged.Overview.Reachability.AllObservers.Should().NotContain(tombstoned);
                        // The subject check is a pin rather than a bite here: the generator only ever
                        // observes nodes every side holds as a member, so no side starts with a record
                        // about a node it has tombstoned. P11 covers the case with teeth, where RemoveAll
                        // strips a node that really was being observed.
                        merged.Overview.Reachability.Records.Select(r => r.Subject)
                            .Should().NotContain(tombstoned,
                                "a record about a removed node is stale, and an unreachable non-member blocks convergence");
                        merged.Overview.Reachability.Versions.Keys.Should().NotContain(tombstoned);

                        if (other.HasMember(tombstoned)) hit = true;
                    }
                }

                if (hit) Interlocked.Increment(ref covered);
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
                var hit = false;

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
                        hit = true;
                    }
                }

                if (hit) Interlocked.Increment(ref covered);
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

                var hit = false;

                for (var side = 0; side < 2; side++)
                {
                    var other = g[1 - side];
                    foreach (var m in g[side].Members)
                    {
                        if (other.HasMember(m.UniqueAddress)) continue;
                        if (!Terminal.Contains(m.Status)) continue;

                        // the tombstone check widened the drop condition with an OR - it did not replace it
                        mergedAddresses.Should().NotContain(m.UniqueAddress);
                        hit = true;
                    }
                }

                if (hit) Interlocked.Increment(ref covered);
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
            Assert.SkipWhen(Cluster.IsAssertInvariantsEnabled, AdversarialSkipReason);

            var covered = 0;

            // Shape.Adversarial lets a side carry the same node as a member and as a tombstone. The Gossip
            // constructor rejects that under AKKA_CLUSTER_ASSERT=on; nothing rejects it otherwise, so the
            // merge is checked against it here.
            Sides(2, Shape.Adversarial).Sample(g =>
            {
                var hit = false;

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

                        hit = true;
                    }
                }

                if (hit) Interlocked.Increment(ref covered);
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
                var hit = false;

                foreach (var (winner, loser) in new[] { (g[0], g[1]), (g[1], g[0]) })
                {
                    var merged = winner.MergeTombstones(loser);

                    // the winner's members are untouched, so disjointness is what the guard buys
                    merged.Members.Select(m => m.UniqueAddress).ToImmutableHashSet()
                        .Intersect(merged.Tombstones.Keys).Should().BeEmpty();
                    merged.Members.SetEquals(winner.Members).Should().BeTrue();

                    // Adopting a tombstone leaves reachability alone, and it does not need to touch it.
                    // The winner only adopts tombstones for nodes it does not hold as a member, and no
                    // gossip carries a reachability record for a non-member subject: Merge filters
                    // subjects through its `allowed` set and RemoveAll strips them outright. If that ever
                    // stopped being true, MergeTombstones would have to strip subjects too - an
                    // unreachable non-member blocks convergence forever, because MembershipState.
                    // Convergence resolves it to a Removed member and Removed is not a status it skips.
                    merged.Overview.Reachability.Records.Select(r => r.Subject)
                        .Should().NotIntersectWith(merged.Tombstones.Keys);

                    foreach (var kv in loser.Tombstones)
                    {
                        if (winner.HasMember(kv.Key))
                        {
                            // Dropped on purpose, and it is a real drop - the winner loses a removal it
                            // was told about. It is safe because ReceiveGossip only calls MergeTombstones
                            // with two equal clocks: a removal bumps the removing node's clock entry, so
                            // both sides descend from every removal either of them knows about, and a
                            // member that is back after a removal is one whose tombstone was already
                            // pruned. Adopting it again would undo that prune.
                            //
                            // The branches that pick a strictly newer gossip do not come through here at
                            // all - they keep the winner's tombstones and nothing else, which is what
                            // makes a prune stick. P15 covers that.
                            merged.Tombstones.Keys.Should().NotContain(kv.Key);
                            hit = true;
                        }
                        else
                        {
                            merged.Tombstones.Keys.Should().Contain(kv.Key);
                        }
                    }
                }

                if (hit) Interlocked.Increment(ref covered);
            }, iter: MergeIterations, print: Print);

            covered.Should().BeGreaterThan(MergeIterations / 10,
                "the generator must actually produce a loser tombstone for a node the winner holds");
        }

        [Fact(DisplayName = "P7d: Merge cannot drop a member both sides hold, even when one side tombstones it")]
        public void P7d_Merge_cannot_decide_a_member_both_sides_hold()
        {
            var covered = 0;

            Assert.SkipWhen(Cluster.IsAssertInvariantsEnabled, AdversarialSkipReason);

            // Pins the precondition the Gossip constructor enforces. PickHighestPriority consults
            // tombstones only on its one-sided branch; a node both sides hold as a member is picked by
            // status alone. So a gossip that carries a member and a tombstone for the same node, merged
            // with a gossip that holds that member, comes out non-disjoint. That is exactly the state
            // AssertInvariants rejects, and why it rejects it.
            Sides(2, Shape.Adversarial).Sample(g =>
            {
                var hit = false;

                for (var side = 0; side < 2; side++)
                {
                    var other = g[1 - side];
                    foreach (var key in g[side].Tombstones.Keys)
                    {
                        if (!g[side].HasMember(key) || !other.HasMember(key)) continue;

                        var merged = g[0].Merge(g[1]);
                        merged.Members.Select(m => m.UniqueAddress).Should().Contain(key);
                        merged.Tombstones.Keys.Should().Contain(key);
                        hit = true;
                    }
                }

                if (hit) Interlocked.Increment(ref covered);
            }, iter: MergeIterations, print: Print);

            covered.Should().BeGreaterThan(MergeIterations / 10,
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

                        // subjects too, at every status - AllUnreachable misses Terminated records, and a
                        // record about a node that is no longer a member stalls convergence: the
                        // convergence check resolves the subject to a Removed member, and Removed is not
                        // one of the statuses it skips
                        removed.Overview.Reachability.Records.Select(r => r.Subject)
                            .Should().NotContain(node);
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
            var mixedStatus = 0;

            Histories().Sample(h =>
            {
                var result = RunHistory(h, checkMonotonicity: false, checkConvergence: true);
                if (result.Removed > 0) Interlocked.Increment(ref removals);
                if (result.MixedStatusRemoval) Interlocked.Increment(ref mixedStatus);
            }, iter: SequenceIterations, print: h => h.ToString());

            removals.Should().BeGreaterThan(SequenceIterations / 2,
                "most histories must actually remove something");

            mixedStatus.Should().BeGreaterThan(SequenceIterations / 10,
                "the status op must actually put peers on a different status than the removing node, " +
                "which is the shape a real removal has");
        }

        [Fact(DisplayName = "P14: a node's tombstone set never shrinks except across a prune")]
        public void P14_Tombstone_sets_only_grow()
        {
            var removals = 0;

            Histories().Sample(h =>
            {
                var result = RunHistory(h, checkMonotonicity: true, checkConvergence: false);
                if (result.Removed > 0) Interlocked.Increment(ref removals);
            }, iter: SequenceIterations, print: h => h.ToString());

            removals.Should().BeGreaterThan(SequenceIterations / 2,
                "most histories must actually remove something");
        }

        // -----------------------------------------------------------------------------------------
        // P15 - a prune sticks
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P15: a tombstone pruned on a converged tick does not come back")]
        public void P15_A_prune_is_not_undone_by_the_next_exchange()
        {
            var pruned = 0;

            Histories().Sample(h =>
            {
                var result = RunHistory(h, checkMonotonicity: false, checkConvergence: true,
                    checkPruneStickiness: true);
                if (result.Pruned > 0) Interlocked.Increment(ref pruned);
            }, iter: SequenceIterations, print: h => h.ToString());

            pruned.Should().BeGreaterThan(SequenceIterations / 10,
                "the drawn cutoff must actually expire tombstones in a good share of histories");
        }

        // -----------------------------------------------------------------------------------------
        // P16 - a week of virtual time, skewed clocks and a moving leader
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P16: tombstones expire and stay expired across a simulated week of skewed clocks")]
        public void P16_Tombstones_expire_across_virtual_time()
        {
            var expiredAndPruned = 0;

            TimeHistories().Sample(h =>
            {
                if (RunTimeHistory(h)) Interlocked.Increment(ref expiredAndPruned);
            }, iter: SequenceIterations, print: h => h.ToString());

            expiredAndPruned.Should().BeGreaterThan(SequenceIterations / 10,
                "histories must actually age a tombstone past the window and prune it mid-history");
        }

        /// <summary>
        /// Histories for P16: the same op alphabet as <see cref="RunHistory"/> plus a jump forward in
        /// virtual time, and a per-node clock offset so no two nodes agree on what time it is.
        /// </summary>
        private static Gen<TimeHistory> TimeHistories() =>
            Gen.Select(
                Gen.Int[3, 5],
                Gen.Int[0, 1199].Array[20, 44],
                Gen.Long[0L, (long)MaxSkew.TotalMilliseconds].Array[NodeCount],
                Gen.Int[0, 4],
                (n, ops, skew, pruner) => new TimeHistory(n, ops, skew, pruner));

        private sealed class TimeHistory
        {
            public TimeHistory(int nodes, int[] ops, long[] skewMillis, int finalPruneNode)
            {
                Nodes = nodes;
                Ops = ops;
                SkewMillis = skewMillis;
                FinalPruneNode = finalPruneNode;
            }

            public int Nodes { get; }
            public int[] Ops { get; }

            /// <summary>How far ahead of true virtual time each node's own clock runs.</summary>
            public long[] SkewMillis { get; }

            public int FinalPruneNode { get; }

            public override string ToString() =>
                $"nodes={Nodes} ops=[{string.Join(",", Ops)}] skew=[{string.Join(",", SkewMillis)}] " +
                $"finalPruner={FinalPruneNode}";
        }

        /// <summary>
        /// Replays a history over a simulated week.
        ///
        /// Three things this adds over <see cref="RunHistory"/>:
        ///
        /// 1. Virtual time. A jump op moves now forward by drawn hours, so a tombstone minted early really
        ///    does age past <see cref="PruneWindow"/> - the shipped 24 hour default, not a cutoff invented
        ///    here. The jumps are sparse rather than a tick per second; every function under test takes its
        ///    instant as a parameter, so sampling instants is as good as stepping through them.
        /// 2. Clock skew. Each node's own clock runs a drawn amount ahead of true virtual time. A removal
        ///    stamps its tombstone with the removing node's skewed view, and a prune computes its cutoff
        ///    from the pruning node's skewed view, so the two ends of the retention window are read off
        ///    different clocks - which is what happens in a real cluster.
        /// 3. A moving leader. The prune op is run by a drawn node, not a fixed one.
        ///
        /// A prune only runs from a converged state, because that is the only state
        /// LeaderActionsOnConvergence runs in. That is what keeps the window from expiring a tombstone
        /// before it has reached everybody, which would put the removed member back - real behaviour, and
        /// the reason the setting defaults to a day, but not what this property is about.
        ///
        /// Returns true when a mid-history prune actually dropped something, so the caller can check the
        /// week-scale part was not a no-op.
        /// </summary>
        private static bool RunTimeHistory(TimeHistory history)
        {
            var n = history.Nodes;
            var window = (long)PruneWindow.TotalMilliseconds;
            var maxSkew = (long)MaxSkew.TotalMilliseconds;

            // start far enough in so a cutoff never goes negative
            var now = window * 2;

            var initialMembers = Enumerable.Range(0, NodeCount)
                .Select(i => MemberOf(i, MemberStatus.Up))
                .ToImmutableSortedSet();

            var initialVersion = Enumerable.Range(0, NodeCount)
                .Aggregate(VectorClock.Create(), (v, i) => v.Increment(VclockNodeOf(Node(i))));

            var gossips = Enumerable.Repeat(
                new Gossip(initialMembers, new GossipOverview(), initialVersion), n).ToArray();
            var identities = Enumerable.Range(0, n).Select(Node).ToArray();

            var oracle = new HashSet<UniqueAddress>();

            // The true virtual instant behind each tombstone timestamp a history has written. Two mints can
            // land on the same key and value from different instants, because the skews differ; the
            // earliest is kept, which only ever weakens the skew-safety bound below - never fakes a pass.
            var mintedAt = new Dictionary<(UniqueAddress Key, long Timestamp), long>();

            var prunedSomethingMidHistory = false;

            bool Converged() => gossips.Select(DescribeCore).Distinct().Count() == 1;

            // one gossip round: every ordered pair exchanges once
            void Round()
            {
                for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    gossips[i] = Receive(gossips[i], gossips[j], identities[i]);
                }
            }

            void Converge()
            {
                var rounds = 0;
                while (rounds++ < 25)
                {
                    var snapshot = gossips.Select(DescribeCore).ToArray();
                    Round();
                    if (gossips.Select(DescribeCore).SequenceEqual(snapshot) && Converged())
                        break;
                }
            }

            void RecordMint(UniqueAddress key, long stamp, long instant)
            {
                if (!mintedAt.TryGetValue((key, stamp), out var earlier) || instant < earlier)
                    mintedAt[(key, stamp)] = instant;
            }

            // Every drop has to satisfy the arithmetic the retention argument rests on: a tombstone is
            // never dropped sooner than window minus the widest clock disagreement after it was written.
            void AssertSkewSafety(Gossip before, Gossip after, long instant)
            {
                foreach (var kv in before.Tombstones)
                {
                    if (after.Tombstones.ContainsKey(kv.Key)) continue;

                    mintedAt.TryGetValue((kv.Key, kv.Value), out var mintInstant).Should().BeTrue(
                        "every tombstone this history holds was written by it");

                    (instant - mintInstant).Should().BeGreaterOrEqualTo(window - maxSkew,
                        "a tombstone for {0} written at {1} must not be dropped at {2}", kv.Key, mintInstant, instant);
                }
            }

            void Prune(int node, long instant)
            {
                var target = gossips[node];
                var cutoff = instant + history.SkewMillis[node] - window;
                var pruned = target.PruneTombstones(cutoff);

                if (ReferenceEquals(pruned, target))
                {
                    target.Tombstones.Values.Where(v => v <= cutoff).Should().BeEmpty(
                        "PruneTombstones handed back the same instance, so nothing may have expired");
                    return;
                }

                AssertSkewSafety(target, pruned, instant);
                gossips[node] = StampLocalChange(pruned, identities[node]);
            }

            foreach (var raw in history.Ops)
            {
                // 0 removes, 1 and 2 exchange a single pair, 3 runs a whole gossip round, 4 jumps time
                // forward, 5 is the leader's converged tick. Whole rounds are in the alphabet because a
                // prune only fires from a converged state, and single pairwise exchanges rarely get there
                // inside a short history.
                switch (raw % 6)
                {
                    case 0:
                    {
                        var actor = raw / 6 % n;
                        var g = gossips[actor];
                        var candidates = g.Members
                            .Where(m => !identities.Contains(m.UniqueAddress))
                            .ToArray();
                        if (candidates.Length == 0) break;

                        var victim = candidates[raw / 60 % candidates.Length].UniqueAddress;
                        var stamp = now + history.SkewMillis[actor];

                        gossips[actor] = StampLocalChange(
                            g.RemoveAll(ImmutableHashSet.Create(victim), stamp), identities[actor]);
                        RecordMint(victim, stamp, now);
                        oracle.Add(victim);
                        break;
                    }

                    case 3:
                        Round();
                        break;

                    case 4:
                        // a sparse jump forward, up to a day and a half at a time
                        now += (1 + raw / 6 % 36) * 60L * 60L * 1000L;
                        break;

                    case 5:
                    {
                        // the leader's converged tick, run by whichever node the draw picked
                        if (!Converged()) break;
                        var node = raw / 6 % n;
                        var before = gossips[node].Tombstones.Count;
                        Prune(node, now);
                        if (gossips[node].Tombstones.Count < before) prunedSomethingMidHistory = true;
                        break;
                    }

                    default:
                    {
                        var receiver = raw / 6 % n;
                        var sender = raw / 60 % n;
                        if (receiver == sender) break;
                        gossips[receiver] = Receive(gossips[receiver], gossips[sender], identities[receiver]);
                        break;
                    }
                }

                for (var i = 0; i < n; i++)
                {
                    var g = gossips[i];
                    var addresses = g.Members.Select(m => m.UniqueAddress).ToImmutableHashSet();

                    addresses.Intersect(g.Tombstones.Keys).Should().BeEmpty(
                        "node {0} must keep members and tombstones disjoint", i);
                    addresses.Except(initialMembers.Select(m => m.UniqueAddress)).Should().BeEmpty();
                    g.Version.Versions.Keys.Should().BeSubsetOf(addresses.Select(VclockNodeOf),
                        "node {0} must not keep a clock entry for a node that is not a member", i);
                }
            }

            Converge();
            Converged().Should().BeTrue("every node must settle on the same members, tombstones and clock");

            // Liveness. Push past the window plus the widest clock disagreement, so every tombstone this
            // history wrote is expired for any node that might prune it, then let a drawn node run its
            // leader tick and the cluster exchange again. Nothing may hand the expired tombstones back.
            now += window + maxSkew + 60L * 60L * 1000L;

            var finalPruner = history.FinalPruneNode % n;
            Prune(finalPruner, now);
            Converge();

            Converged().Should().BeTrue("the cluster must settle again after the prune");

            var expectedMembers = initialMembers
                .Where(m => !oracle.Contains(m.UniqueAddress))
                .Select(m => m.UniqueAddress)
                .ToImmutableHashSet();

            foreach (var g in gossips)
            {
                g.Tombstones.Should().BeEmpty(
                    "every tombstone is past the retention window, and a peer that has not pruned must not hand one back");

                g.Members.Select(m => m.UniqueAddress).ToImmutableHashSet()
                    .SetEquals(expectedMembers).Should().BeTrue(
                        "dropping an expired tombstone must not put a removed member back once the removal has reached everyone");
            }

            return prunedSomethingMidHistory;
        }

        // -----------------------------------------------------------------------------------------
        // P17 - the node that minted a tombstone is removed too
        // -----------------------------------------------------------------------------------------

        [Fact(DisplayName = "P17: a removal holds after the node that recorded it is itself removed")]
        public void P17_Tombstones_outlive_the_node_that_minted_them()
        {
            var lineageErased = 0;

            LineageHistories().Sample(h =>
            {
                if (RunLineageHistory(h)) Interlocked.Increment(ref lineageErased);
            }, iter: LineageIterations, print: h => h.ToString());

            lineageErased.Should().BeGreaterThan(LineageIterations / 10,
                "histories must actually remove a node that had itself removed someone");
        }

        /// <summary>
        /// Histories for P17. Every node gossips and every node can be removed, including the one that
        /// recorded somebody else's removal.
        /// </summary>
        private static Gen<History> LineageHistories() =>
            Gen.Select(Gen.Int[4, NodeCount], Gen.Int[0, 1199].Array[30, 60],
                (n, ops) => new History(n, ops, 0L, 0));

        /// <summary>
        /// The case <see cref="RunHistory"/> leaves out: a removal may target a node that gossips, so the
        /// node that recorded a tombstone can itself be removed afterwards.
        ///
        /// That matters because <see cref="Gossip.Merge"/> prunes the clock entry of every tombstoned
        /// node. A tombstone is written with a bump to the recording node's own clock entry, and that bump
        /// is the evidence a later gossip descends from the removal. Remove the recording node and the
        /// evidence is pruned out of both sides' clocks - so this is where a gossip could, in principle,
        /// come out strictly newer than a tombstone carrier without ever having seen the removal.
        ///
        /// A removed node stops gossiping the moment anyone removes it, the way a real one shuts down. A
        /// node that has not heard about the removal may still remove the same node again from its own
        /// view, which is what a real cluster does through its own leader, so a second tombstone with a
        /// later timestamp is a normal thing for a history to produce.
        ///
        /// One restriction, and it earns its keep. A node only removes somebody when its own clock is at
        /// or ahead of every other survivor's - the model's stand-in for the rule that
        /// LeaderActionsOnConvergence only runs on a converged tick. Without it the model reaches a
        /// resurrection that predates tombstones and that no cluster can actually reach:
        ///
        ///     four nodes a, b, c, d. b removes a: b writes a's tombstone, prunes a's clock entry and
        ///     bumps its own. c picks that up. d never does. d then removes b, which prunes b's clock
        ///     entry - the one bump that recorded a's removal - while d still carries a's own clock entry,
        ///     because d never saw a go. d's clock now strictly dominates c's, so c adopts d's gossip
        ///     whole and a walks back in as a member.
        ///
        /// A real d cannot get there: removing b is a leader action and needs convergence, convergence
        /// needs every unreachable member to be Down or Exiting, and d holds the dead a at Up. d has to
        /// Down a first, and then it removes a itself and writes its own tombstone for it. The
        /// restriction is on the model, not on production - the same history resurrects a with the
        /// tombstone union restored on the winner-picked branches, so this is not something the union was
        /// buying.
        ///
        /// Returns true when the history reached the shape it is named after: a node that recorded a
        /// tombstone was itself removed later.
        /// </summary>
        private static bool RunLineageHistory(History history)
        {
            var n = history.Nodes;

            var initialMembers = Enumerable.Range(0, n)
                .Select(i => MemberOf(i, MemberStatus.Up))
                .ToImmutableSortedSet();

            var initialVersion = Enumerable.Range(0, n)
                .Aggregate(VectorClock.Create(), (v, i) => v.Increment(VclockNodeOf(Node(i))));

            var gossips = Enumerable.Repeat(
                new Gossip(initialMembers, new GossipOverview(), initialVersion), n).ToArray();
            var identities = Enumerable.Range(0, n).Select(Node).ToArray();

            var alive = Enumerable.Repeat(true, n).ToArray();
            var recorded = new HashSet<int>();       // nodes that have written at least one tombstone
            var lineageErased = false;
            var timestamp = 1L;

            int[] Live() => Enumerable.Range(0, n).Where(i => alive[i]).ToArray();

            foreach (var raw in history.Ops)
            {
                var live = Live();

                // 0 removes, 1 through 4 exchange, 5 downs. Exchanges dominate on purpose: a removal
                // needs its actor to have caught up with everyone, and only an exchange gets it there.
                switch (raw % 6)
                {
                    case 0:
                    {
                        // a node removes a member of its own view - possibly one that still gossips, and
                        // possibly one somebody else already removed
                        if (live.Length <= 2) break;

                        var actor = live[raw / 6 % live.Length];

                        // only a node that is not behind anyone removes: see the restriction note above
                        var caughtUp = live.All(i => i == actor ||
                                                     gossips[actor].Version.CompareTo(gossips[i].Version)
                                                         is VectorClock.Ordering.Same or VectorClock.Ordering.After);
                        if (!caughtUp) break;

                        var g = gossips[actor];
                        var candidates = g.Members
                            .Where(m => !m.UniqueAddress.Equals(identities[actor]))
                            .ToArray();
                        if (candidates.Length == 0) break;

                        var victim = candidates[raw / 60 % candidates.Length].UniqueAddress;
                        var victimIndex = Array.IndexOf(identities, victim);

                        gossips[actor] = StampLocalChange(
                            g.RemoveAll(ImmutableHashSet.Create(victim), timestamp++), identities[actor]);

                        recorded.Add(actor);
                        if (victimIndex >= 0 && alive[victimIndex])
                        {
                            alive[victimIndex] = false;
                            if (recorded.Contains(victimIndex)) lineageErased = true;
                        }

                        break;
                    }

                    case 5:
                    {
                        var node = live[raw / 6 % live.Length];
                        var g = gossips[node];
                        var candidates = g.Members
                            .Where(m => m.Status == MemberStatus.Up &&
                                        !m.UniqueAddress.Equals(identities[node]))
                            .ToArray();
                        if (candidates.Length == 0) break;

                        var toDown = candidates[raw / 60 % candidates.Length];
                        gossips[node] = StampLocalChange(
                            g.Copy(members: g.Members.Remove(toDown).Add(toDown.Copy(MemberStatus.Down))),
                            identities[node]);
                        break;
                    }

                    default:
                    {
                        if (live.Length < 2) break;
                        var receiver = live[raw / 6 % live.Length];
                        var sender = live[raw / 60 % live.Length];
                        if (receiver == sender) break;
                        gossips[receiver] = Receive(gossips[receiver], gossips[sender], identities[receiver]);
                        break;
                    }
                }

                foreach (var i in Live())
                {
                    var g = gossips[i];
                    var addresses = g.Members.Select(m => m.UniqueAddress).ToImmutableHashSet();

                    addresses.Intersect(g.Tombstones.Keys).Should().BeEmpty(
                        "node {0} must keep members and tombstones disjoint", i);

                    g.Version.Versions.Keys.Should().BeSubsetOf(addresses.Select(VclockNodeOf),
                        "node {0} must not keep a clock entry for a node that is not a member", i);
                }
            }

            // exchange between the survivors until nothing moves
            var survivors = Live();
            var rounds = 0;
            while (rounds++ < 25)
            {
                var snapshot = survivors.Select(i => DescribeCore(gossips[i])).ToArray();

                foreach (var i in survivors)
                foreach (var j in survivors)
                {
                    if (i == j) continue;
                    gossips[i] = Receive(gossips[i], gossips[j], identities[i]);
                }

                if (survivors.Select(i => DescribeCore(gossips[i])).SequenceEqual(snapshot) &&
                    snapshot.Distinct().Count() == 1)
                    break;
            }

            var states = survivors.Select(i => DescribeCore(gossips[i])).Distinct().ToArray();
            states.Length.Should().Be(1,
                "every survivor must end on the same members, tombstones and clock, got:\n{0}",
                string.Join("\n", states));

            var expectedMembers = survivors.Select(i => identities[i]).ToImmutableHashSet();
            var removed = Enumerable.Range(0, n).Where(i => !alive[i]).Select(i => identities[i])
                .ToImmutableHashSet();

            foreach (var i in survivors)
            {
                var g = gossips[i];

                g.Members.Select(m => m.UniqueAddress).ToImmutableHashSet()
                    .SetEquals(expectedMembers).Should().BeTrue(
                        "a removed node must not come back, not even one that had recorded a removal itself");

                g.Tombstones.Keys.ToImmutableHashSet().SetEquals(removed).Should().BeTrue(
                    "every removal must still be on the record, including one whose recording node was removed after it");
            }

            return lineageErased;
        }

        /// <summary>
        /// A random op sequence over N virtual nodes, each holding a gossip over the same initial
        /// membership. Ops are ints, decoded by <see cref="RunHistory"/>.
        ///
        /// A history also carries the cutoff and the node for the converged-tick prune P15 runs. Removal
        /// timestamps start at 1 and step by one, and a history writes at most a handful of them, so a
        /// cutoff drawn from [0, 11] lands below, inside and above that range.
        /// </summary>
        private static Gen<History> Histories() =>
            Gen.Select(Gen.Int[3, 5], Gen.Int[0, 1199].Array[10, 30], Gen.Long[0L, 11L], Gen.Int[0, 4],
                (n, ops, cutoff, pruner) => new History(n, ops, cutoff, pruner));

        private sealed class History
        {
            public History(int nodes, int[] ops, long pruneCutoff, int pruneNode)
            {
                Nodes = nodes;
                Ops = ops;
                PruneCutoff = pruneCutoff;
                PruneNode = pruneNode;
            }

            public int Nodes { get; }
            public int[] Ops { get; }
            public long PruneCutoff { get; }
            public int PruneNode { get; }

            public override string ToString() =>
                $"nodes={Nodes} ops=[{string.Join(",", Ops)}] cutoff={PruneCutoff} pruner={PruneNode}";
        }

        /// <summary>What a replayed history did, so a property can check it was not a no-op.</summary>
        private readonly struct HistoryResult
        {
            public HistoryResult(int removed, int pruned, bool mixedStatusRemoval)
            {
                Removed = removed;
                Pruned = pruned;
                MixedStatusRemoval = mixedStatusRemoval;
            }

            /// <summary>How many nodes the oracle saw removed.</summary>
            public int Removed { get; }

            /// <summary>How many tombstones the converged-tick prune dropped.</summary>
            public int Pruned { get; }

            /// <summary>
            /// Whether the history removed a node while some other node held it at a different status -
            /// the shape a real removal has, and the reason the status op exists.
            /// </summary>
            public bool MixedStatusRemoval { get; }
        }

        /// <summary>
        /// Replays a history against N gossips and an oracle - a plain set of removed UIDs. Returns how
        /// many nodes the oracle saw removed, so the caller can check the histories were not all no-ops.
        ///
        /// Exchanges use the same branch selection <c>ClusterCoreDaemon.ReceiveGossip</c> uses, so the
        /// property covers the paths a real cluster takes, not just <see cref="Gossip.Merge"/>.
        /// Timestamps come from a counter passed in here, never from the wall clock.
        ///
        /// Four op kinds: a removal, an exchange, a prune, and a status change that pushes one node's
        /// view of one member to Down. The status change is what gives a removal its real shape - by the
        /// time a leader removes a node, its peers hold that node at a mix of Up and Down, and the
        /// tombstone has to propagate through that mix.
        ///
        /// Two deliberate restrictions on what a history can build:
        ///
        /// 1. The N virtual nodes stay in the cluster for the whole history: a removal never targets one
        ///    of them. A node that keeps gossiping after being removed is not a history the cluster
        ///    produces - the removed node shuts down, and <c>ReceiveGossip</c> drops gossip that does not
        ///    contain the receiver and gossip from a sender the receiver no longer holds, so a zombie is
        ///    deaf and ignored in both directions. Modelling one instead would only measure that
        ///    omission. P17 covers removing a gossiping node, including the node that minted a
        ///    tombstone.
        /// 2. Every node starts Up. Statuses only ever move towards Down from there, through the status
        ///    op, so no history opens on a member set the cluster could not have reached.
        /// </summary>
        private static HistoryResult RunHistory(History history, bool checkMonotonicity, bool checkConvergence,
            bool checkPruneStickiness = false)
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
            var mixedStatusRemoval = false;

            // a cutoff below every timestamp this history will ever write, so the prune op expires nothing
            const long QuietCutoff = 0L;

            foreach (var raw in history.Ops)
            {
                var before = gossips.Select(g => g.Tombstones.Keys.ToImmutableHashSet()).ToArray();
                var kind = raw % 4;
                var isPrune = kind == 2;

                switch (kind)
                {
                    case 0:
                    {
                        // the leader removes a live member that is not one of the gossiping nodes
                        var actor = raw / 4 % n;
                        var g = gossips[actor];
                        var candidates = g.Members
                            .Where(m => !identities.Contains(m.UniqueAddress))
                            .ToArray();
                        if (candidates.Length == 0) break;

                        var victimMember = candidates[raw / 40 % candidates.Length];
                        var victim = victimMember.UniqueAddress;

                        // did anyone else hold the victim at a different status at this moment?
                        for (var i = 0; i < n && !mixedStatusRemoval; i++)
                        {
                            if (i == actor) continue;
                            var peer = gossips[i].Members.FirstOrDefault(m => m.UniqueAddress.Equals(victim));
                            if (peer != null && peer.Status != victimMember.Status) mixedStatusRemoval = true;
                        }

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
                        var receiver = raw / 4 % n;
                        var sender = raw / 40 % n;
                        if (receiver == sender) break;
                        gossips[receiver] = Receive(gossips[receiver], gossips[sender], identities[receiver]);
                        break;
                    }

                    case 2:
                    {
                        var node = raw / 4 % n;
                        var pruned = gossips[node].PruneTombstones(QuietCutoff);

                        // nothing expired, so the leader's no-op guard must hold
                        pruned.Should().BeSameAs(gossips[node]);
                        gossips[node] = pruned;
                        break;
                    }

                    default:
                    {
                        // One node moves one member to Down in its own view. Only that node sees it until
                        // the change gossips out, which is how a peer ends up holding a node the leader is
                        // about to remove at a different status than the leader does.
                        var node = raw / 4 % n;
                        var g = gossips[node];
                        var candidates = g.Members
                            .Where(m => m.Status == MemberStatus.Up)
                            .ToArray();
                        if (candidates.Length == 0) break;

                        var toDown = candidates[raw / 40 % candidates.Length];
                        var downed = g.Copy(members: g.Members.Remove(toDown).Add(toDown.Copy(MemberStatus.Down)));
                        gossips[node] = StampLocalChange(downed, identities[node]);
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

                    // (c) the clock never accumulates entries for nodes that are not members. This is the
                    // model's copy of the "Too many vector clock entries in gossip state" check
                    // ClusterCoreDaemon.AssertLatestGossip runs, stated as the set inclusion it really
                    // means rather than a count: every entry belongs to a node that is still a member.
                    // A removal prunes the entry of the node it removes, Merge prunes the entry of every
                    // tombstoned node, and the concurrent branch prunes the entry of a one-sided Down or
                    // Exiting member it is about to drop. Miss any of those and the gossip grows a tail of
                    // dead nodes that rides every message forever.
                    var memberClockNodes = addresses.Select(VclockNodeOf).ToImmutableHashSet();
                    g.Version.Versions.Keys.Should().BeSubsetOf(memberClockNodes,
                        "node {0} must not keep a clock entry for a node that is not a member", i);
                }

                if (checkMonotonicity && !isPrune)
                {
                    for (var i = 0; i < n; i++)
                        before[i].Except(gossips[i].Tombstones.Keys).Should().BeEmpty(
                            "node {0} must not lose a tombstone outside a prune", i);
                }
            }

            if (!checkConvergence) return new HistoryResult(oracle.Count, 0, mixedStatusRemoval);

            // exchange every ordered pair until nothing moves
            void Converge()
            {
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
            }

            void AssertConverged()
            {
                var states = gossips.Select(DescribeCore).Distinct().ToArray();
                states.Length.Should().Be(1,
                    "every node must end on the same members, tombstones and clock, got:\n{0}",
                    string.Join("\n", states));
            }

            Converge();
            AssertConverged();

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

            if (!checkPruneStickiness) return new HistoryResult(oracle.Count, 0, mixedStatusRemoval);

            // P15. Every node now holds the same members, tombstones and clock - the state the leader is
            // in when it prunes, because LeaderActionsOnConvergence only runs on a converged tick. Prune
            // one node with the drawn cutoff, stamp it the way UpdateLatestGossip does, and let the
            // cluster exchange again. The stamp puts the pruning node strictly ahead of every peer, so
            // every exchange from here takes a winner-picked branch - which is the branch that used to
            // hand the pruned tombstones straight back.
            var pruner = history.PruneNode % n;
            var target = gossips[pruner];
            var afterPrune = target.PruneTombstones(history.PruneCutoff);

            if (ReferenceEquals(afterPrune, target))
            {
                target.Tombstones.Values.Where(v => v <= history.PruneCutoff).Should().BeEmpty(
                    "PruneTombstones handed back the same instance, so nothing may have expired");
                return new HistoryResult(oracle.Count, 0, mixedStatusRemoval);
            }

            var dropped = target.Tombstones.Keys.Except(afterPrune.Tombstones.Keys).ToImmutableHashSet();
            gossips[pruner] = StampLocalChange(afterPrune, identities[pruner]);

            Converge();
            AssertConverged();

            var survivors = oracle.Except(dropped).ToImmutableHashSet();

            foreach (var g in gossips)
            {
                foreach (var key in dropped)
                    g.Tombstones.Keys.Should().NotContain(key,
                        "a tombstone pruned by the leader must not be handed back by a peer that never pruned");

                g.Tombstones.Keys.ToImmutableHashSet().SetEquals(survivors).Should().BeTrue(
                    "the prune drops the expired tombstones and keeps the rest");

                // the prune dropped tombstones for nodes no gossip still holds as a member, so a dropped
                // tombstone cannot let a member walk back in
                g.Members.Select(m => m.UniqueAddress).ToImmutableHashSet()
                    .SetEquals(expectedMembers).Should().BeTrue();
            }

            return new HistoryResult(oracle.Count, dropped.Count, mixedStatusRemoval);
        }

        /// <summary>
        /// Mirrors <c>ClusterCoreDaemon.UpdateLatestGossip</c>: stamp the change with the node's own clock
        /// entry, then mark the gossip as seen by nobody but this node.
        /// </summary>
        private static Gossip StampLocalChange(Gossip gossip, UniqueAddress self) =>
            gossip.Increment(VclockNodeOf(self)).OnlySeen(self);

        /// <summary>
        /// One gossip exchange, run through the production branch selection rather than a copy of it.
        /// <see cref="ClusterCoreDaemon.SelectWinningGossip"/> is the code path a real node takes for
        /// every gossip it receives; only the parts <c>ReceiveGossip</c> does around it - the talkback
        /// decision, logging, and marking the result as seen - are left out. Calling it directly is what
        /// makes these sequence properties fail when that branch selection changes.
        /// </summary>
        private static Gossip Receive(Gossip local, Gossip remote, UniqueAddress self)
        {
            var comparison = remote.Version.CompareTo(local.Version);
            var winner = ClusterCoreDaemon.SelectWinningGossip(local, remote, comparison);
            return winner.Seen(self);
        }
    }
}
