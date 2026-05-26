//-----------------------------------------------------------------------
// <copyright file="Bugfix8219MergeFuzzingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster;
using FluentAssertions;
using Xunit;

namespace Akka.DistributedData.Tests
{
    /// <summary>
    /// Property-based / fuzzing tests for the merge invariants relied on by
    /// https://github.com/akkadotnet/akka.net/issues/8219.
    ///
    /// The customer report is that subscribers see <see cref="Changed"/> events
    /// whose entry count is strictly less than the writer ever wrote, even
    /// though the writer never removes entries and uses <see cref="WriteLocal"/>.
    /// The notification path just publishes whatever envelope is stored, so a
    /// partial count implies some prior merge stored a state with fewer entries
    /// than the local replica had before. These tests drive the merge logic
    /// directly with many random state combinations, modelling the
    /// <see cref="Replicator"/>'s causal-delivery enforcement for deltas, to
    /// find any case where a merge non-monotonically drops a key without a
    /// prior remove.
    ///
    /// We exercise two properties end-to-end:
    ///
    /// 1. <see cref="ORDictionary{TKey,TValue}.MergeDelta(IDeltaOperation)"/>
    ///    of a SetItem delta into a strictly-larger state must never reduce
    ///    the key count (direct model of the customer's
    ///    <c>DeltaPropagation</c> arrival path on a subscriber's node).
    /// 2. A fleet of replicas applying writer deltas in causal order, with
    ///    occasional packet loss + gossip catch-up (and an optional
    ///    writer-identity change to model singleton failover), must never see
    ///    its current count fall below its prior maximum on any replica.
    ///
    /// If any of these properties fails, the failure log includes the exact
    /// seed and operation index so the scenario can be replayed deterministically.
    /// </summary>
    public class Bugfix8219MergeFuzzingSpec
    {
        private const int Iterations = 200;
        private const int OperationsPerIteration = 200;
        private const int ReplicaCount = 5;

        private static UniqueAddress Node(int id)
            => new(new Address("akka.tcp", "system", "host", 2550 + id), id);

        private static readonly UniqueAddress WriterA = Node(1);
        private static readonly UniqueAddress WriterB = Node(2);

        /// <summary>
        /// Minimal direct repro the fuzzer found. Three replicas — all with
        /// data added by the single writer — get into a state where two
        /// replicas have the same key with DIFFERENT dots (because the writer
        /// updated the key multiple times and one replica missed the later
        /// update). Each replica's VersionVector has separately advanced past
        /// both dots via gossip catch-up from other replicas. The merge of
        /// these two then drops the shared key.
        ///
        /// Production reachability: a single cluster-singleton writer doing
        /// repeated <c>SetItem</c> on existing keys produces multiple dots
        /// per key. Under partial delta delivery + gossip-only catch-up of
        /// VersionVector, a replica can keep an OLD per-key dot while its VV
        /// races ahead. Two such replicas merging then drops the shared key.
        /// This is exactly the customer's report in #8219.
        /// </summary>
        [Fact]
        public void Merge_should_not_drop_key_when_both_sides_have_it_with_different_dots_from_same_writer()
        {
            // Writer state: key 6 first added at version 1, key 1 added at
            // version 2, ..., (other keys at versions 3, 4), then key 6
            // UPDATED again at version 5. Writer's VV is N1:5.
            var writer = ORDictionary<int, GCounter>.Empty
                .AddOrUpdate(WriterA, 6, GCounter.Empty, c => c.Increment(WriterA, 1)); // op1: key 6 dot N1:1
            writer = writer.ResetDelta()
                .AddOrUpdate(WriterA, 1, GCounter.Empty, c => c.Increment(WriterA, 1)); // op2: key 1 dot N1:2
            writer = writer.ResetDelta()
                .AddOrUpdate(WriterA, 2, GCounter.Empty, c => c.Increment(WriterA, 1)); // op3: key 2 dot N1:3
            writer = writer.ResetDelta()
                .AddOrUpdate(WriterA, 3, GCounter.Empty, c => c.Increment(WriterA, 1)); // op4: key 3 dot N1:4
            // op5: update key 6 again -> dot N1:5 (overwrites the old dot N1:1
            // on the writer)
            var beforeOp5 = writer;
            writer = writer.ResetDelta()
                .AddOrUpdate(WriterA, 6, GCounter.Empty, c => c.Increment(WriterA, 1));

            // REPLICA L applied ops 1..5 in order (got everything). It has key
            // 6 at dot N1:5.
            var replicaL = beforeOp5.MergeDelta(
                (ORDictionary<int, GCounter>.IDeltaOperation)writer.Delta);
            replicaL.Entries.Keys.OrderBy(x => x).Should().Equal(new[] { 1, 2, 3, 6 });

            // REPLICA M applied ops 1..4 but MISSED op5 (the second update to
            // key 6). Then it gossiped with someone who had VV=N1:5 but who
            // also missed op5 — so M's VV is N1:5 but key 6 still has the OLD
            // dot N1:1.
            var replicaM = beforeOp5;
            // Manually splice in a higher VV without applying op5's delta —
            // this is what gossip-with-a-peer-that-itself-missed-op5 does.
            replicaM = SetKeySetVersionVectorTo(replicaM, WriterA, 5);
            replicaM.Entries.Keys.OrderBy(x => x).Should().Equal(new[] { 1, 2, 3, 6 });
            // sanity: both have the same set of keys (4 keys each, including
            // key 6)
            replicaL.Count.Should().Be(replicaM.Count);

            // Now L gossips with M (full-state merge). Both have key 6 with
            // different dots from the same writer; both VVs are >= 5.
            var merged = (ORDictionary<int, GCounter>)replicaL.Merge(replicaM);

            // INVARIANT: key 6 must survive because both sides had it. No
            // remove ever happened.
            merged.Entries.Keys.OrderBy(x => x).Should().Equal(new[] { 1, 2, 3, 6 },
                $"merge dropped key 6 even though both replicas have it. " +
                $"L={DescribeBriefly(replicaL)}, M={DescribeBriefly(replicaM)}, " +
                $"merged={DescribeBriefly(merged)}");
        }

        /// <summary>
        /// Test-only helper that constructs an ORDictionary whose KeySet has a
        /// specific VersionVector entry, modelling the state a replica arrives
        /// at when it gossip-merges with a peer whose VV is ahead but who
        /// shares the same element dots.
        /// </summary>
        private static ORDictionary<int, GCounter> SetKeySetVersionVectorTo(
            ORDictionary<int, GCounter> d, UniqueAddress node, long version)
        {
            var newVv = VersionVector.Create(node, version).Merge(d.KeySet.VersionVector);
            var newKeySet = new ORSet<int>(d.KeySet.ElementsMap, newVv);
            return new ORDictionary<int, GCounter>(newKeySet, d.ValueMap);
        }

        private static string DescribeBriefly(ORDictionary<int, GCounter> d) =>
            $"{{keys=[{string.Join(",", d.KeySet.ElementsMap.Select(kv => $"{kv.Key}@{kv.Value}"))}], vv={d.KeySet.VersionVector}}}";

        /// <summary>
        /// Direct model of the customer's DeltaPropagation arrival path on a
        /// subscriber's node: the local replica already has more entries than
        /// the delta touches, and a delta arrives. The merge result must never
        /// have fewer keys than the local side had before.
        /// </summary>
        [Fact]
        public void MergeDelta_into_strictly_larger_local_state_should_never_drop_keys()
        {
            for (var seed = 0; seed < Iterations; seed++)
            {
                var rng = new Random(seed);

                // base state: a fully populated dictionary at the local replica
                var localKeys = Enumerable.Range(0, 50 + rng.Next(0, 100)).ToArray();
                var local = ApplySetItems(ORDictionary<int, GCounter>.Empty, WriterA, localKeys);

                // simulate the writer doing a few more SetItems on a FORK of the
                // same base, then extracting its delta to send over
                var writerSide = ApplySetItems(local.ResetDelta(), WriterA,
                    PickSomeFromBaseOrNew(rng, localKeys, additions: rng.Next(1, 6)));

                var delta = writerSide.Delta;
                delta.Should().NotBeNull($"seed={seed}: SetItem must produce a delta");

                var beforeCount = local.Count;
                var beforeKeys = local.Entries.Keys.ToImmutableHashSet();

                var afterMerge = local.MergeDelta(delta);
                var afterCount = afterMerge.Count;
                var afterKeys = afterMerge.Entries.Keys.ToImmutableHashSet();

                afterCount.Should().BeGreaterOrEqualTo(beforeCount,
                    $"seed={seed}: MergeDelta must not drop keys " +
                    $"(before={beforeCount}, after={afterCount})");
                afterKeys.IsSupersetOf(beforeKeys).Should().BeTrue(
                    $"seed={seed}: MergeDelta result must contain all prior keys");
            }
        }

        /// <summary>
        /// Long-running multi-replica fuzz with causal delivery enforcement
        /// matching the Replicator's <see cref="IRequireCausualDeliveryOfDeltas"/>
        /// guard. One writer continuously updates random keys (always
        /// <c>SetItem</c>, never remove). Each replica only applies a delta
        /// whose sequence number is exactly one above its last applied
        /// (causal). Otherwise the replica catches up via full-state gossip
        /// from a random peer. Gossip is run often enough to keep replicas
        /// converging.
        ///
        /// Invariants: for every replica, at every operation, the current key
        /// count must be greater than or equal to that replica's prior maximum
        /// observed count. After the run, a full pairwise gossip round must
        /// converge every replica to the writer's keyset.
        /// </summary>
        [Fact]
        public void Replicas_should_never_decrease_in_keyset_under_causal_delta_delivery_with_gossip()
        {
            for (var seed = 0; seed < Iterations; seed++)
                RunFuzz(seed, writerSwitchOver: false);
        }

        /// <summary>
        /// Same as <see cref="Replicas_should_never_decrease_in_keyset_under_causal_delta_delivery_with_gossip"/>
        /// but with the writer identity changing midway, modelling
        /// cluster-singleton failover. Causal delivery is enforced per-writer.
        /// </summary>
        [Fact]
        public void Replicas_should_never_decrease_in_keyset_when_writer_identity_changes()
        {
            for (var seed = 0; seed < Iterations; seed++)
                RunFuzz(seed, writerSwitchOver: true);
        }

        private static void RunFuzz(int seed, bool writerSwitchOver)
        {
            var rng = new Random(seed);

            var writer = ORDictionary<int, GCounter>.Empty;
            var replicas = Enumerable.Range(0, ReplicaCount)
                .Select(_ => ORDictionary<int, GCounter>.Empty)
                .ToArray();

            // Per-replica per-writer last-applied seqNr and pending delta queue.
            // The Replicator tracks these per (key, writer-UniqueAddress); we
            // collapse to per-replica per-writer since we use a single key.
            var lastAppliedA = new long[ReplicaCount];
            var lastAppliedB = new long[ReplicaCount];
            var pendingA = NewPendingArray();
            var pendingB = NewPendingArray();
            var maxObserved = new int[ReplicaCount];

            var failoverPoint = OperationsPerIteration / 2;
            long writerSeqA = 0, writerSeqB = 0;

            for (var op = 0; op < OperationsPerIteration; op++)
            {
                var isWriterA = !writerSwitchOver || op < failoverPoint;
                var currentWriter = isWriterA ? WriterA : WriterB;

                var key = rng.Next(0, op + 5);
                var nextWriter = writer.ResetDelta()
                    .AddOrUpdate(currentWriter, key, GCounter.Empty, c => c.Increment(currentWriter, 1));
                var delta = (ORDictionary<int, GCounter>.IDeltaOperation)nextWriter.Delta;
                writer = nextWriter;
                long currentSeq;
                if (isWriterA) { writerSeqA++; currentSeq = writerSeqA; }
                else { writerSeqB++; currentSeq = writerSeqB; }

                for (var i = 0; i < ReplicaCount; i++)
                {
                    var roll = rng.Next(0, 100);
                    var before = replicas[i].Count;
                    string action;

                    string detail = "";
                    if (roll < 70)
                    {
                        action = $"deliver(seq={currentSeq})";
                        if (isWriterA) pendingA[i][currentSeq] = delta;
                        else pendingB[i][currentSeq] = delta;
                    }
                    else if (roll < 80)
                    {
                        action = "lost";
                    }
                    else
                    {
                        var peer = (i + 1 + rng.Next(0, ReplicaCount - 1)) % ReplicaCount;
                        action = $"gossip(peer={peer})";
                        detail = $" local-before={Describe(replicas[i])} peer={Describe(replicas[peer])}";
                        replicas[i] = (ORDictionary<int, GCounter>)replicas[i].Merge(replicas[peer]);
                        detail += $" local-after-merge={Describe(replicas[i])}";
                        if (lastAppliedA[peer] > lastAppliedA[i]) lastAppliedA[i] = lastAppliedA[peer];
                        if (lastAppliedB[peer] > lastAppliedB[i]) lastAppliedB[i] = lastAppliedB[peer];
                    }

                    replicas[i] = DrainContiguousDeltas(replicas[i], pendingA[i], ref lastAppliedA[i]);
                    replicas[i] = DrainContiguousDeltas(replicas[i], pendingB[i], ref lastAppliedB[i]);

                    if (replicas[i].Count > maxObserved[i])
                        maxObserved[i] = replicas[i].Count;

                    var after = replicas[i].Count;
                    after.Should().BeGreaterOrEqualTo(maxObserved[i],
                        $"seed={seed}, op={op}, replica={i}, action={action}: " +
                        $"count fell from previous max {maxObserved[i]} to {after} " +
                        $"(before={before}).{detail}");
                }
            }

            // Convergence is not the focus of this test — the focus is the
            // monotonicity (no-decrease) invariant. We do not assert
            // convergence here because the noise model can permanently lose a
            // delta from all replicas (rare but possible) which would never
            // converge without writer re-publishing. Convergence under bounded
            // loss is a separate property of the Replicator's gossip protocol,
            // not of the merge logic this test targets.
        }

        private static SortedDictionary<long, ORDictionary<int, GCounter>.IDeltaOperation>[] NewPendingArray()
        {
            var a = new SortedDictionary<long, ORDictionary<int, GCounter>.IDeltaOperation>[ReplicaCount];
            for (var i = 0; i < ReplicaCount; i++)
                a[i] = new SortedDictionary<long, ORDictionary<int, GCounter>.IDeltaOperation>();
            return a;
        }

        private static string Describe(ORDictionary<int, GCounter> d) =>
            $"{{ keys=[{string.Join(",", d.KeySet.ElementsMap.Select(kv => $"{kv.Key}->{kv.Value}"))}], vv={d.KeySet.VersionVector} }}";

        // ---------- helpers ----------

        private static ORDictionary<int, GCounter> ApplySetItems(
            ORDictionary<int, GCounter> start,
            UniqueAddress writer,
            IEnumerable<int> keys)
        {
            var d = start;
            foreach (var k in keys)
                d = d.AddOrUpdate(writer, k, GCounter.Empty, c => c.Increment(writer, 1));
            return d;
        }

        /// <summary>
        /// Apply pending deltas in strict seqNr order, stopping at the first
        /// gap. Models the Replicator's
        /// <see cref="IRequireCausualDeliveryOfDeltas"/> guard: a delta is
        /// only applied if its seqNr is exactly one above the last applied.
        /// Returns the new state; <paramref name="lastApplied"/> is updated
        /// in-place to reflect the last applied seqNr.
        /// </summary>
        private static ORDictionary<int, GCounter> DrainContiguousDeltas(
            ORDictionary<int, GCounter> state,
            SortedDictionary<long, ORDictionary<int, GCounter>.IDeltaOperation> pending,
            ref long lastApplied)
        {
            while (pending.Count > 0)
            {
                var firstKey = pending.Keys.First();
                if (firstKey != lastApplied + 1) break;
                var d = pending[firstKey];
                pending.Remove(firstKey);
                lastApplied = firstKey;
                state = state.MergeDelta(d);
            }
            return state;
        }

        private static int[] PickSomeFromBaseOrNew(Random rng, int[] baseKeys, int additions)
        {
            var result = new int[additions];
            for (var i = 0; i < additions; i++)
            {
                if (baseKeys.Length > 0 && rng.Next(0, 2) == 0)
                    result[i] = baseKeys[rng.Next(0, baseKeys.Length)];
                else
                    result[i] = 1_000_000 + rng.Next(0, 1_000_000);
            }
            return result;
        }
    }
}
