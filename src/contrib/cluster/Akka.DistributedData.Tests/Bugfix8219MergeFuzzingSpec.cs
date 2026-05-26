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
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Xunit;

#pragma warning disable xUnit1028
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
    /// The deterministic minimal repro (<see cref="Merge_should_not_drop_key_when_both_sides_have_it_with_different_dots_from_same_writer"/>)
    /// is a plain <see cref="FactAttribute"/> for fast, focused regression
    /// coverage. The <see cref="PropertyAttribute"/> tests use FsCheck for
    /// broader random exploration of the merge surface — they discovered the
    /// minimal repro originally.
    /// </summary>
    public class Bugfix8219MergeFuzzingSpec
    {
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
            // key 6). Then it gossiped with someone whose VV had reached
            // writer's current version — but that peer also missed op5 — so
            // M's VV catches up to writer's VV while key 6 still has its old
            // dot. We use writer's actual current version because
            // VersionVector.Increment uses a PROCESS-WIDE atomic counter
            // (VersionVector.cs:51), so the absolute version numbers depend
            // on test ordering. Using writer's own version keeps the test
            // deterministic across that counter offset.
            var writerVersion = writer.KeySet.VersionVector.VersionAt(WriterA);
            var replicaM = SetKeySetVersionVectorTo(beforeOp5, WriterA, writerVersion);
            replicaM.Entries.Keys.OrderBy(x => x).Should().Equal(new[] { 1, 2, 3, 6 });
            replicaL.Count.Should().Be(replicaM.Count);

            // Now L gossips with M (full-state merge). Both have key 6 with
            // different dots from the same writer; both VVs are >= writer's
            // current version.
            var merged = (ORDictionary<int, GCounter>)replicaL.Merge(replicaM);

            // INVARIANT: key 6 must survive because both sides had it. No
            // remove ever happened.
            merged.Entries.Keys.OrderBy(x => x).Should().Equal(new[] { 1, 2, 3, 6 },
                "merge dropped key 6 even though both replicas have it. " +
                $"L={DescribeBriefly(replicaL)}, M={DescribeBriefly(replicaM)}, " +
                $"merged={DescribeBriefly(merged)}");
        }

        /// <summary>
        /// Property: <see cref="ORDictionary{TKey,TValue}.MergeDelta"/> of a
        /// SetItem delta into a strictly-larger state must never reduce the
        /// key count. This is the closest direct model of the customer's
        /// DeltaPropagation path on a subscriber's node: local already has
        /// more keys than the delta touches.
        /// </summary>
        [Property(MaxTest = 500, Arbitrary = new[] { typeof(DDataGenerators) })]
        public Property MergeDelta_must_not_drop_keys_when_local_has_a_superset(WriterSetItem[] localOps, WriterSetItem[] deltaOps)
        {
            // build a local state from a sequence of SetItem ops by WriterA
            var local = ApplySetItems(ORDictionary<int, GCounter>.Empty, WriterA, localOps.Select(o => o.Key));

            // build a delta by applying additional SetItems on the same base.
            // ResetDelta clears the inherited delta so the resulting Delta is
            // just the new ops (matching the customer's writer pattern).
            var writerSide = ApplySetItems(local.ResetDelta(), WriterA, deltaOps.Select(o => o.Key));
            var delta = writerSide.Delta;
            if (delta is null) return true.ToProperty(); // no delta -> vacuously true

            var before = local.Entries.Keys.ToImmutableHashSet();
            var afterMerge = local.MergeDelta(delta);
            var after = afterMerge.Entries.Keys.ToImmutableHashSet();

            return after.IsSupersetOf(before)
                .Label($"MergeDelta dropped keys. before=[{string.Join(",", before)}], after=[{string.Join(",", after)}]");
        }

        /// <summary>
        /// Property (single writer, no failover): a fleet of replicas applying
        /// the writer's deltas in causal order, with occasional packet loss
        /// and gossip catch-up, must never see its current key count fall
        /// below its prior maximum on any replica. This is the fuzz that
        /// originally found the bug — captured as
        /// <see cref="Merge_should_not_drop_key_when_both_sides_have_it_with_different_dots_from_same_writer"/>.
        /// </summary>
        [Property(MaxTest = 200, Arbitrary = new[] { typeof(DDataGenerators) })]
        public Property Replicas_never_decrease_under_causal_delta_delivery_with_gossip(WriterSetItem[] ops, int actionSeed)
        {
            return RunReplicaFuzz(ops, actionSeed, writerSwitchOver: false);
        }

        /// <summary>
        /// Property: same as <see cref="Replicas_never_decrease_under_causal_delta_delivery_with_gossip"/>
        /// but with the writer identity changing midway, modelling
        /// cluster-singleton failover. Causal delivery is enforced per-writer.
        /// </summary>
        [Property(MaxTest = 200, Arbitrary = new[] { typeof(DDataGenerators) })]
        public Property Replicas_never_decrease_when_writer_identity_changes(WriterSetItem[] ops, int actionSeed)
        {
            return RunReplicaFuzz(ops, actionSeed, writerSwitchOver: true);
        }

        private const int ReplicaCount = 5;

        private static Property RunReplicaFuzz(WriterSetItem[] ops, int actionSeed, bool writerSwitchOver)
        {
            // We use a derived Random for the network actions so that the
            // network behaviour is reproducible from the (ops, actionSeed)
            // pair that FsCheck generated. FsCheck shrinks the (ops, seed)
            // pair on failure; the simulation is deterministic given them.
            var rng = new Random(actionSeed);

            var writer = ORDictionary<int, GCounter>.Empty;
            var replicas = Enumerable.Range(0, ReplicaCount)
                .Select(_ => ORDictionary<int, GCounter>.Empty)
                .ToArray();

            var lastAppliedA = new long[ReplicaCount];
            var lastAppliedB = new long[ReplicaCount];
            var pendingA = NewPendingArray();
            var pendingB = NewPendingArray();
            var maxObserved = new int[ReplicaCount];

            var failoverPoint = ops.Length / 2;
            long writerSeqA = 0, writerSeqB = 0;

            for (var op = 0; op < ops.Length; op++)
            {
                var isWriterA = !writerSwitchOver || op < failoverPoint;
                var currentWriter = isWriterA ? WriterA : WriterB;
                var key = ops[op].Key;

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
                    if (roll < 70)
                    {
                        if (isWriterA) pendingA[i][currentSeq] = delta;
                        else pendingB[i][currentSeq] = delta;
                    }
                    else if (roll < 80)
                    {
                        // 10%: lost in transit
                    }
                    else
                    {
                        var peer = (i + 1 + rng.Next(0, ReplicaCount - 1)) % ReplicaCount;
                        replicas[i] = (ORDictionary<int, GCounter>)replicas[i].Merge(replicas[peer]);
                        if (lastAppliedA[peer] > lastAppliedA[i]) lastAppliedA[i] = lastAppliedA[peer];
                        if (lastAppliedB[peer] > lastAppliedB[i]) lastAppliedB[i] = lastAppliedB[peer];
                    }

                    replicas[i] = DrainContiguousDeltas(replicas[i], pendingA[i], ref lastAppliedA[i]);
                    replicas[i] = DrainContiguousDeltas(replicas[i], pendingB[i], ref lastAppliedB[i]);

                    if (replicas[i].Count > maxObserved[i])
                        maxObserved[i] = replicas[i].Count;

                    if (replicas[i].Count < maxObserved[i])
                    {
                        return false.ToProperty().Label(
                            $"op={op}, replica={i}: count fell from {maxObserved[i]} to {replicas[i].Count}. " +
                            $"state={DescribeBriefly(replicas[i])}");
                    }
                }
            }

            return true.ToProperty();
        }

        // ---------- helpers ----------

        private static SortedDictionary<long, ORDictionary<int, GCounter>.IDeltaOperation>[] NewPendingArray()
        {
            var a = new SortedDictionary<long, ORDictionary<int, GCounter>.IDeltaOperation>[ReplicaCount];
            for (var i = 0; i < ReplicaCount; i++)
                a[i] = new SortedDictionary<long, ORDictionary<int, GCounter>.IDeltaOperation>();
            return a;
        }

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

        /// <summary>
        /// Test-only helper that constructs an ORDictionary whose KeySet has
        /// a specific VersionVector entry, modelling the state a replica
        /// arrives at when it gossip-merges with a peer whose VV is ahead but
        /// who shares the same element dots.
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
    }
}
