//-----------------------------------------------------------------------
// <copyright file="MergeFuzzingMachine.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Cluster;
using Akka.DistributedData.Internal;
using FsCheck;
using FsCheck.Experimental;
using FsCheck.Fluent;

namespace Akka.DistributedData.Tests
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// FsCheck model-based testing machine for fuzzing the merge invariants
    /// of <see cref="ORDictionary{TKey,TValue}"/> under simulated cluster
    /// activity. Reproduction target: https://github.com/akkadotnet/akka.net/issues/8219.
    ///
    /// Faithful to production by using <see cref="DataEnvelope"/> directly —
    /// every merge goes through the same envelope-level merge the
    /// <c>Replicator</c> uses, including <c>DeltaVersions</c> max-merge,
    /// <c>PruningPerformed</c>-state propagation, and the <c>Cleaned</c>
    /// dot-rewriting applied to both sides before the data-level merge.
    ///
    /// Operations:
    /// <list type="bullet">
    ///   <item><c>WriterSetItem(key)</c> — current writer applies
    ///         <c>AddOrUpdate(key)</c>, producing a new delta with the
    ///         writer's next per-writer sequence number.</item>
    ///   <item><c>DeliverDelta(replicaIdx, deltaIdx)</c> — apply a previously-
    ///         produced delta to a replica via envelope merge.
    ///         <b>Preconditioned on the Replicator's
    ///         <see cref="IRequireCausualDeliveryOfDeltas"/> rule</b>: the
    ///         delta's per-writer seqNr must be exactly one above the
    ///         replica's last-applied seqNr for that writer. Out-of-order
    ///         or duplicate delivery is refused, matching the production
    ///         NACK/skip behaviour.</item>
    ///   <item><c>GossipBetween(target, source)</c> — full envelope merge
    ///         (target = target.Merge(source)). Propagates pruning state
    ///         and applies <c>Cleaned</c> on both sides.</item>
    ///   <item><c>PerformPruning(replicaIdx, prunedW, intoW)</c> — replica
    ///         initialises pruning then performs it, mirroring the
    ///         Replicator's two-phase prune lifecycle.</item>
    ///   <item><c>RestartReplica(replicaIdx)</c> — replica's envelope wiped
    ///         (no-durable-storage restart).</item>
    ///   <item><c>ChangeWriterIdentity</c> — switch writer identity,
    ///         modelling singleton failover. Each writer has independent
    ///         seqNr stream.</item>
    /// </list>
    ///
    /// Invariant: at every step, every replica's keyset must be a superset
    /// of all keys it has ever been told about (directly via delivered
    /// delta, transitively via gossip from a peer that knew the key, or
    /// preserved across pruning since pruning rewrites dots without
    /// removing elements). Because no remove operation exists, keysets
    /// must only ever grow.
    /// </summary>
    [InternalApi]
    public sealed class MergeFuzzingMachine : Machine<MergeFuzzingMachine.ReplicaCluster, MergeFuzzingMachine.ReplicaClusterModel>
    {
        public const int ReplicaCount = 5;
        public const int KeyUniverseMax = 10;
        public const int WriterIdentityCount = 2;

        private static UniqueAddress MakeUniqueAddress(int id)
            => new(new Address("akka.tcp", "system", "host", 2550 + id), id);

        private static readonly UniqueAddress[] _writers =
            Enumerable.Range(1, WriterIdentityCount).Select(MakeUniqueAddress).ToArray();

        public override Arbitrary<Setup<ReplicaCluster, ReplicaClusterModel>> Setup =>
            Arb.From(Gen.Constant((Setup<ReplicaCluster, ReplicaClusterModel>)new ReplicaClusterSetup()));

        public override Gen<Operation<ReplicaCluster, ReplicaClusterModel>> Next(ReplicaClusterModel model)
        {
            var gens = new List<Gen<Operation<ReplicaCluster, ReplicaClusterModel>>>
            {
                WriterSetItem.Generator(),
                RestartReplica.Generator(model),
            };

            if (model.DeltaKeys.Length > 0)
            {
                gens.Add(DeliverDelta.Generator(model));
                gens.Add(ChangeWriterIdentity.Generator());
                gens.Add(PerformPruning.Generator(model));
            }

            if (model.ReplicaCount >= 2)
                gens.Add(GossipBetween.Generator(model));

            return Gen.OneOf(gens.ToArray());
        }

        // ---------- Setup ----------

        public sealed class ReplicaClusterSetup : Setup<ReplicaCluster, ReplicaClusterModel>
        {
            public override ReplicaCluster Actual() =>
                new ReplicaCluster(ReplicaCount, _writers);

            public override ReplicaClusterModel Model() =>
                new ReplicaClusterModel(
                    ReplicaCount: ReplicaCount,
                    ReplicaKnownKeys: Enumerable.Repeat(ImmutableHashSet<int>.Empty, ReplicaCount).ToImmutableArray(),
                    DeltaKeys: ImmutableArray<int>.Empty,
                    DeltaWriters: ImmutableArray<int>.Empty,
                    DeltaWriterSeqs: ImmutableArray<long>.Empty,
                    WriterNextSeq: Enumerable.Repeat(0L, WriterIdentityCount).ToImmutableArray(),
                    LastAppliedSeq: Enumerable.Repeat(
                        Enumerable.Repeat(0L, WriterIdentityCount).ToImmutableArray(),
                        ReplicaCount).ToImmutableArray(),
                    PrunedWriters: Enumerable.Repeat(ImmutableHashSet<int>.Empty, ReplicaCount).ToImmutableArray(),
                    CurrentWriterIdx: 0);
        }

        // ---------- Model ----------

        public sealed record ReplicaClusterModel(
            int ReplicaCount,
            ImmutableArray<ImmutableHashSet<int>> ReplicaKnownKeys,
            ImmutableArray<int> DeltaKeys,              // DeltaKeys[i] = key added by delta i
            ImmutableArray<int> DeltaWriters,           // DeltaWriters[i] = writer-identity index that produced delta i
            ImmutableArray<long> DeltaWriterSeqs,       // DeltaWriterSeqs[i] = per-writer seqNr of delta i
            ImmutableArray<long> WriterNextSeq,         // next seqNr each writer will assign
            ImmutableArray<ImmutableArray<long>> LastAppliedSeq, // [replica][writerIdx] = last applied seqNr from that writer on that replica
            ImmutableArray<ImmutableHashSet<int>> PrunedWriters, // [replica] = set of writer indexes for which this replica has performed pruning
            int CurrentWriterIdx)
        {
            public override string ToString()
            {
                var known = string.Join("; ",
                    ReplicaKnownKeys.Select((s, i) => $"R{i}=[{string.Join(",", s.OrderBy(x => x))}]"));
                return $"Model(deltas={DeltaKeys.Length}, known={{{known}}}, writer=W{CurrentWriterIdx})";
            }
        }

        // ---------- Actual ----------

        public sealed class ReplicaCluster
        {
            // Each replica is a DataEnvelope to faithfully model the
            // Replicator's _dataEntries[key].envelope. Envelope merge
            // handles pruning state, DeltaVersions, and Cleaned()
            // dot-rewriting exactly as production does.
            public DataEnvelope[] Replicas { get; set; }
            public DataEnvelope WriterEnvelope { get; set; }
            // Per-delta: the envelope to be merged into a receiving
            // replica (envelope.Data = delta operation, plus DeltaVersions
            // identifying this writer's seqNr).
            public List<DataEnvelope> WriterDeltas { get; }
            public UniqueAddress[] WriterIdentities { get; }
            public int CurrentWriterIdx { get; set; }

            public ReplicaCluster(int replicaCount, UniqueAddress[] writerIdentities)
            {
                Replicas = Enumerable.Range(0, replicaCount)
                    .Select(_ => new DataEnvelope(ORDictionary<int, GCounter>.Empty))
                    .ToArray();
                WriterEnvelope = new DataEnvelope(ORDictionary<int, GCounter>.Empty);
                WriterDeltas = new List<DataEnvelope>();
                WriterIdentities = writerIdentities;
                CurrentWriterIdx = 0;
            }
        }

        // ---------- Operations ----------

        public sealed class WriterSetItem : Operation<ReplicaCluster, ReplicaClusterModel>
        {
            public static Gen<Operation<ReplicaCluster, ReplicaClusterModel>> Generator() =>
                Gen.Choose(0, KeyUniverseMax - 1)
                    .Select(k => (Operation<ReplicaCluster, ReplicaClusterModel>)new WriterSetItem(k));

            public int Key { get; }
            public WriterSetItem(int key) { Key = key; }

            public override bool Pre(ReplicaClusterModel _) => true;

            public override ReplicaClusterModel Run(ReplicaClusterModel model)
            {
                var w = model.CurrentWriterIdx;
                var nextSeq = model.WriterNextSeq[w] + 1;
                return model with
                {
                    DeltaKeys = model.DeltaKeys.Add(Key),
                    DeltaWriters = model.DeltaWriters.Add(w),
                    DeltaWriterSeqs = model.DeltaWriterSeqs.Add(nextSeq),
                    WriterNextSeq = model.WriterNextSeq.SetItem(w, nextSeq),
                };
            }

            public override Property Check(ReplicaCluster actual, ReplicaClusterModel model)
            {
                var w = actual.CurrentWriterIdx;
                var writerAddr = actual.WriterIdentities[w];
                var prevData = (ORDictionary<int, GCounter>)actual.WriterEnvelope.Data;
                var newData = prevData.ResetDelta()
                    .AddOrUpdate(writerAddr, Key, GCounter.Empty, c => c.Increment(writerAddr, 1));
                var newSeq = model.DeltaWriterSeqs[model.DeltaWriterSeqs.Length - 1];

                // Writer's local envelope: full state + DeltaVersions advanced
                // to the new seqNr for this writer.
                actual.WriterEnvelope = new DataEnvelope(
                    data: newData,
                    deltaVersions: actual.WriterEnvelope.DeltaVersions.Merge(VersionVector.Create(writerAddr, newSeq)));

                // Delta envelope to propagate: data is the delta operation,
                // DeltaVersions identifies this writer at the new seqNr.
                actual.WriterDeltas.Add(new DataEnvelope(
                    data: newData.Delta!,
                    deltaVersions: VersionVector.Create(writerAddr, newSeq)));

                return CheckInvariant(actual, model);
            }

            public override string ToString() => $"WriterSetItem({Key})";
        }

        public sealed class DeliverDelta : Operation<ReplicaCluster, ReplicaClusterModel>
        {
            public static Gen<Operation<ReplicaCluster, ReplicaClusterModel>> Generator(ReplicaClusterModel model) =>
                Gen.Choose(0, model.ReplicaCount - 1)
                    .Zip(Gen.Choose(0, model.DeltaKeys.Length - 1))
                    .Select(t => (Operation<ReplicaCluster, ReplicaClusterModel>)new DeliverDelta(t.Item1, t.Item2));

            public int ReplicaIdx { get; }
            public int DeltaIdx { get; }
            public DeliverDelta(int replicaIdx, int deltaIdx) { ReplicaIdx = replicaIdx; DeltaIdx = deltaIdx; }

            public override bool Pre(ReplicaClusterModel model)
            {
                if (DeltaIdx < 0 || DeltaIdx >= model.DeltaKeys.Length) return false;
                var w = model.DeltaWriters[DeltaIdx];
                // Replicator's IsNodeRemoved drop-guard (Replicator.cs:1039):
                // a DeltaPropagation from a writer this replica has pruned
                // is dropped (because the envelope's Pruning map contains
                // that writer). Model this by refusing delivery.
                if (model.PrunedWriters[ReplicaIdx].Contains(w)) return false;
                var deltaSeq = model.DeltaWriterSeqs[DeltaIdx];
                var lastApplied = model.LastAppliedSeq[ReplicaIdx][w];
                // Replicator's IRequireCausualDeliveryOfDeltas rule: applies
                // iff seqNr == lastApplied + 1.
                return deltaSeq == lastApplied + 1;
            }

            public override ReplicaClusterModel Run(ReplicaClusterModel model)
            {
                var w = model.DeltaWriters[DeltaIdx];
                var deltaSeq = model.DeltaWriterSeqs[DeltaIdx];
                var key = model.DeltaKeys[DeltaIdx];
                var newKnown = model.ReplicaKnownKeys.SetItem(
                    ReplicaIdx, model.ReplicaKnownKeys[ReplicaIdx].Add(key));
                var newReplicaSeqs = model.LastAppliedSeq[ReplicaIdx].SetItem(w, deltaSeq);
                var newLastApplied = model.LastAppliedSeq.SetItem(ReplicaIdx, newReplicaSeqs);
                return model with { ReplicaKnownKeys = newKnown, LastAppliedSeq = newLastApplied };
            }

            public override Property Check(ReplicaCluster actual, ReplicaClusterModel model)
            {
                actual.Replicas[ReplicaIdx] = actual.Replicas[ReplicaIdx].Merge(actual.WriterDeltas[DeltaIdx]);
                return CheckInvariant(actual, model);
            }

            public override string ToString() => $"DeliverDelta(replica={ReplicaIdx}, delta={DeltaIdx})";
        }

        public sealed class GossipBetween : Operation<ReplicaCluster, ReplicaClusterModel>
        {
            public static Gen<Operation<ReplicaCluster, ReplicaClusterModel>> Generator(ReplicaClusterModel model) =>
                Gen.Choose(0, model.ReplicaCount - 1)
                    .Zip(Gen.Choose(0, model.ReplicaCount - 1))
                    .Where(t => t.Item1 != t.Item2)
                    .Select(t => (Operation<ReplicaCluster, ReplicaClusterModel>)new GossipBetween(t.Item1, t.Item2));

            public int Target { get; }
            public int Source { get; }
            public GossipBetween(int target, int source) { Target = target; Source = source; }

            public override bool Pre(ReplicaClusterModel _) => Target != Source;

            public override ReplicaClusterModel Run(ReplicaClusterModel model)
            {
                var newKnown = model.ReplicaKnownKeys.SetItem(
                    Target, model.ReplicaKnownKeys[Target].Union(model.ReplicaKnownKeys[Source]));
                // Envelope merge takes max of DeltaVersions on both sides,
                // so target's lastApplied per writer becomes union max.
                var targetSeqs = model.LastAppliedSeq[Target];
                var sourceSeqs = model.LastAppliedSeq[Source];
                var mergedSeqs = targetSeqs.Zip(sourceSeqs, Math.Max).ToImmutableArray();
                var newLastApplied = model.LastAppliedSeq.SetItem(Target, mergedSeqs);
                // Pruning state propagates: target absorbs source's pruned-
                // writer set (DataEnvelope.Merge merges the Pruning map).
                var newPruned = model.PrunedWriters.SetItem(
                    Target, model.PrunedWriters[Target].Union(model.PrunedWriters[Source]));
                return model with { ReplicaKnownKeys = newKnown, LastAppliedSeq = newLastApplied, PrunedWriters = newPruned };
            }

            public override Property Check(ReplicaCluster actual, ReplicaClusterModel model)
            {
                actual.Replicas[Target] = actual.Replicas[Target].Merge(actual.Replicas[Source]);
                return CheckInvariant(actual, model);
            }

            public override string ToString() => $"GossipBetween(target={Target}, source={Source})";
        }

        public sealed class ChangeWriterIdentity : Operation<ReplicaCluster, ReplicaClusterModel>
        {
            public static Gen<Operation<ReplicaCluster, ReplicaClusterModel>> Generator() =>
                Gen.Constant((Operation<ReplicaCluster, ReplicaClusterModel>)new ChangeWriterIdentity());

            public override bool Pre(ReplicaClusterModel _) => true;

            public override ReplicaClusterModel Run(ReplicaClusterModel model) =>
                model with { CurrentWriterIdx = (model.CurrentWriterIdx + 1) % WriterIdentityCount };

            public override Property Check(ReplicaCluster actual, ReplicaClusterModel model)
            {
                actual.CurrentWriterIdx = (actual.CurrentWriterIdx + 1) % WriterIdentityCount;
                return CheckInvariant(actual, model);
            }

            public override string ToString() => "ChangeWriterIdentity";
        }

        /// <summary>
        /// Models the Replicator's two-phase pruning: init (mark pruning
        /// state with <c>PruningInitialized</c>) then perform (rewrite
        /// dots and clear DeltaVersions for the removed writer). We collapse
        /// the two phases into one op because the seen-by-all check between
        /// them is dissemination bookkeeping, not data-affecting logic.
        /// Per-replica because production pruning is NOT atomic across the
        /// cluster — different replicas perform <c>Prune</c> at different
        /// times, and the gap is the dissemination window. Pruning state
        /// (Performed) propagates via gossip, and <c>DataEnvelope.Merge</c>
        /// applies <c>Cleaned</c> to both sides during gossip merge.
        /// </summary>
        public sealed class PerformPruning : Operation<ReplicaCluster, ReplicaClusterModel>
        {
            public static Gen<Operation<ReplicaCluster, ReplicaClusterModel>> Generator(ReplicaClusterModel model) =>
                Gen.Choose(0, model.ReplicaCount - 1)
                    .Zip(Gen.Choose(0, WriterIdentityCount - 1))
                    .Zip(Gen.Choose(0, WriterIdentityCount - 1))
                    .Select(t => (Operation<ReplicaCluster, ReplicaClusterModel>)new PerformPruning(t.Item1.Item1, t.Item1.Item2, t.Item2));

            public int ReplicaIdx { get; }
            public int PrunedWriterIdx { get; }
            public int CollapseIntoWriterIdx { get; }

            public PerformPruning(int replicaIdx, int prunedWriterIdx, int collapseIntoWriterIdx)
            {
                ReplicaIdx = replicaIdx;
                PrunedWriterIdx = prunedWriterIdx;
                CollapseIntoWriterIdx = collapseIntoWriterIdx;
            }

            public override bool Pre(ReplicaClusterModel model) =>
                PrunedWriterIdx != CollapseIntoWriterIdx
                // Only meaningful to prune if this replica has data from the
                // pruned writer (mirrors production: pruning is for removed
                // nodes whose dots are present in the data).
                && model.LastAppliedSeq[ReplicaIdx][PrunedWriterIdx] > 0;

            public override ReplicaClusterModel Run(ReplicaClusterModel model)
            {
                // Pruning rewrites dots, doesn't remove elements — keys
                // stay. DataEnvelope.Prune calls CleanedDeltaVersions(from)
                // which removes the pruned writer's entry from DeltaVersions
                // (i.e. LastAppliedSeq[r][prunedW] resets to 0). The
                // envelope's Pruning map gains a PruningPerformed entry for
                // the pruned writer — which is what the Replicator's
                // IsNodeRemoved check consults to drop late deltas from
                // that writer.
                var newReplicaSeqs = model.LastAppliedSeq[ReplicaIdx].SetItem(PrunedWriterIdx, 0L);
                var newLastApplied = model.LastAppliedSeq.SetItem(ReplicaIdx, newReplicaSeqs);
                var newPruned = model.PrunedWriters.SetItem(
                    ReplicaIdx, model.PrunedWriters[ReplicaIdx].Add(PrunedWriterIdx));
                return model with { LastAppliedSeq = newLastApplied, PrunedWriters = newPruned };
            }

            public override Property Check(ReplicaCluster actual, ReplicaClusterModel model)
            {
                var prunedAddr = actual.WriterIdentities[PrunedWriterIdx];
                var collapseAddr = actual.WriterIdentities[CollapseIntoWriterIdx];
                var envelope = actual.Replicas[ReplicaIdx];
                envelope = envelope.InitRemovedNodePruning(prunedAddr, collapseAddr);
                envelope = envelope.Prune(prunedAddr, new PruningPerformed(DateTime.UtcNow.AddDays(1)));
                actual.Replicas[ReplicaIdx] = envelope;
                return CheckInvariant(actual, model);
            }

            public override string ToString() =>
                $"PerformPruning(replica={ReplicaIdx}, prunedW={PrunedWriterIdx}, into=W{CollapseIntoWriterIdx})";
        }

        /// <summary>
        /// Models a replica restarting WITHOUT durable persistence: local
        /// envelope wiped. DeltaVersions for every writer goes back to zero
        /// (a missing envelope returns 0 from <c>GetDeltaSequenceNr</c>).
        /// The replica then has to catch up via gossip.
        /// </summary>
        public sealed class RestartReplica : Operation<ReplicaCluster, ReplicaClusterModel>
        {
            public static Gen<Operation<ReplicaCluster, ReplicaClusterModel>> Generator(ReplicaClusterModel model) =>
                Gen.Choose(0, model.ReplicaCount - 1)
                    .Select(r => (Operation<ReplicaCluster, ReplicaClusterModel>)new RestartReplica(r));

            public int ReplicaIdx { get; }
            public RestartReplica(int replicaIdx) { ReplicaIdx = replicaIdx; }

            public override bool Pre(ReplicaClusterModel _) => true;

            public override ReplicaClusterModel Run(ReplicaClusterModel model)
            {
                var newKnown = model.ReplicaKnownKeys.SetItem(ReplicaIdx, ImmutableHashSet<int>.Empty);
                var resetSeqs = Enumerable.Repeat(0L, WriterIdentityCount).ToImmutableArray();
                var newLastApplied = model.LastAppliedSeq.SetItem(ReplicaIdx, resetSeqs);
                // Restart with no durable storage also drops the pruning
                // state — fresh envelope has no Pruning entries.
                var newPruned = model.PrunedWriters.SetItem(ReplicaIdx, ImmutableHashSet<int>.Empty);
                return model with { ReplicaKnownKeys = newKnown, LastAppliedSeq = newLastApplied, PrunedWriters = newPruned };
            }

            public override Property Check(ReplicaCluster actual, ReplicaClusterModel model)
            {
                actual.Replicas[ReplicaIdx] = new DataEnvelope(ORDictionary<int, GCounter>.Empty);
                return CheckInvariant(actual, model);
            }

            public override string ToString() => $"RestartReplica({ReplicaIdx})";
        }

        // ---------- Invariant ----------

        private static Property CheckInvariant(ReplicaCluster actual, ReplicaClusterModel model)
        {
            for (var i = 0; i < model.ReplicaCount; i++)
            {
                var data = (ORDictionary<int, GCounter>)actual.Replicas[i].Data;
                var actualKeys = data.Entries.Keys.ToImmutableHashSet();
                var requiredKeys = model.ReplicaKnownKeys[i];
                if (!actualKeys.IsSupersetOf(requiredKeys))
                {
                    var missing = requiredKeys.Except(actualKeys);
                    return false.ToProperty().Label(
                        $"replica {i} dropped key(s) [{string.Join(",", missing)}]. " +
                        $"required ⊇ [{string.Join(",", requiredKeys)}], got [{string.Join(",", actualKeys)}]");
                }
            }
            return true.ToProperty();
        }
    }
}
