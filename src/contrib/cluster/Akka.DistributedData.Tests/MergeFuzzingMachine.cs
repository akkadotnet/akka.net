//-----------------------------------------------------------------------
// <copyright file="MergeFuzzingMachine.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Cluster;
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
    /// Operations:
    /// <list type="bullet">
    ///   <item><c>WriterSetItem(key)</c> — the current writer applies
    ///         <c>AddOrUpdate(key)</c>, producing a new delta with the
    ///         writer's next per-writer sequence number.</item>
    ///   <item><c>DeliverDelta(replicaIdx, deltaIdx)</c> — apply a previously-
    ///         produced writer delta to a replica via
    ///         <see cref="ORDictionary{TKey,TValue}.MergeDelta"/>.
    ///         <b>Preconditioned on the Replicator's
    ///         <see cref="IRequireCausualDeliveryOfDeltas"/> rule</b>: the
    ///         delta's per-writer sequence number must be exactly one above
    ///         the replica's last-applied seqNr for that writer (no gap, no
    ///         re-apply). Production replicators NACK any delta that violates
    ///         this; the Machine refuses to even propose such an operation,
    ///         so every counterexample the Machine finds is reachable through
    ///         a production-faithful sequence of events.</item>
    ///   <item><c>GossipBetween(target, source)</c> — full-state
    ///         <c>Merge</c> of source into target. Like the production
    ///         gossip merge, this also advances target's per-writer
    ///         last-applied seqNr to the max of both sides (because
    ///         <see cref="Internal.DataEnvelope"/> merges its
    ///         <c>DeltaVersions</c>).</item>
    ///   <item><c>ChangeWriterIdentity</c> — switch the writer's
    ///         <see cref="UniqueAddress"/>, modelling singleton failover.
    ///         Each writer has its own independent seqNr stream.</item>
    /// </list>
    ///
    /// Invariant: at every step, every replica's keyset must be a superset
    /// of all keys it has ever been told about (directly via delivered
    /// delta or transitively via gossip from a peer that knew the key).
    /// Because no remove operation exists, keysets must only ever grow.
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
            };

            if (model.DeltaKeys.Length > 0)
            {
                gens.Add(DeliverDelta.Generator(model));
                gens.Add(ChangeWriterIdentity.Generator());
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
            int CurrentWriterIdx)
        {
            public override string ToString()
            {
                var known = string.Join("; ",
                    ReplicaKnownKeys.Select((s, i) => $"R{i}=[{string.Join(",", s.OrderBy(x => x))}]"));
                var seqs = string.Join("; ",
                    LastAppliedSeq.Select((arr, i) => $"R{i}={{{string.Join(",", arr.Select((v, w) => $"W{w}:{v}"))}}}"));
                return $"Model(deltas={DeltaKeys.Length}, known={{{known}}}, seqs={{{seqs}}}, writer=W{CurrentWriterIdx})";
            }
        }

        // ---------- Actual ----------

        public sealed class ReplicaCluster
        {
            public ORDictionary<int, GCounter>[] Replicas { get; }
            public ORDictionary<int, GCounter> WriterState { get; set; }
            public List<ORDictionary<int, GCounter>.IDeltaOperation> WriterDeltas { get; }
            public UniqueAddress[] WriterIdentities { get; }
            public int CurrentWriterIdx { get; set; }

            public ReplicaCluster(int replicaCount, UniqueAddress[] writerIdentities)
            {
                Replicas = Enumerable.Range(0, replicaCount)
                    .Select(_ => ORDictionary<int, GCounter>.Empty)
                    .ToArray();
                WriterState = ORDictionary<int, GCounter>.Empty;
                WriterDeltas = new List<ORDictionary<int, GCounter>.IDeltaOperation>();
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
                var writer = actual.WriterIdentities[actual.CurrentWriterIdx];
                actual.WriterState = actual.WriterState.ResetDelta()
                    .AddOrUpdate(writer, Key, GCounter.Empty, c => c.Increment(writer, 1));
                actual.WriterDeltas.Add((ORDictionary<int, GCounter>.IDeltaOperation)actual.WriterState.Delta);
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
                var deltaSeq = model.DeltaWriterSeqs[DeltaIdx];
                var lastApplied = model.LastAppliedSeq[ReplicaIdx][w];
                // Production Replicator's IRequireCausualDeliveryOfDeltas rule:
                // a delta is applied iff its seqNr == lastApplied + 1.
                // Out-of-order or duplicate delivery is NACKed/skipped.
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
                var delta = actual.WriterDeltas[DeltaIdx];
                actual.Replicas[ReplicaIdx] = actual.Replicas[ReplicaIdx].MergeDelta(delta);
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
                // Gossip merges DeltaVersions (max), so target's per-writer
                // lastApplied seqNr advances to the union max with source's.
                // This is what allows a replica's currentSeqNr to advance
                // without the corresponding delta being applied — the same
                // mechanic that lets the production system reach the bug.
                var targetSeqs = model.LastAppliedSeq[Target];
                var sourceSeqs = model.LastAppliedSeq[Source];
                var mergedSeqs = targetSeqs.Zip(sourceSeqs, System.Math.Max).ToImmutableArray();
                var newLastApplied = model.LastAppliedSeq.SetItem(Target, mergedSeqs);
                return model with { ReplicaKnownKeys = newKnown, LastAppliedSeq = newLastApplied };
            }

            public override Property Check(ReplicaCluster actual, ReplicaClusterModel model)
            {
                actual.Replicas[Target] = (ORDictionary<int, GCounter>)actual.Replicas[Target].Merge(actual.Replicas[Source]);
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

        // ---------- Invariant ----------

        private static Property CheckInvariant(ReplicaCluster actual, ReplicaClusterModel model)
        {
            for (var i = 0; i < model.ReplicaCount; i++)
            {
                var actualKeys = actual.Replicas[i].Entries.Keys.ToImmutableHashSet();
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
