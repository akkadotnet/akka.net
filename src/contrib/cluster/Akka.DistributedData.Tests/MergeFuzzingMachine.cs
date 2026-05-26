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
    ///   <item><c>WriterSetItem(key)</c> — current writer does <c>AddOrUpdate</c>.</item>
    ///   <item><c>DeliverDelta(replicaIdx, deltaIdx)</c> — apply a previously-
    ///         produced writer delta to a replica via <c>MergeDelta</c>.</item>
    ///   <item><c>GossipBetween(target, source)</c> — full-state
    ///         <c>Merge</c> of source into target.</item>
    ///   <item><c>ChangeWriterIdentity</c> — switch the writer's
    ///         <see cref="UniqueAddress"/>, modelling singleton failover.</item>
    /// </list>
    ///
    /// Invariant: at every step, every replica's keyset must be a superset
    /// of all keys that operation has ever told it about (directly via
    /// delivered delta or transitively via gossip from a peer that knew
    /// the key). Because no remove operation exists, keysets must only
    /// ever grow.
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

            if (model.WriterKeys.Length > 0)
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
                    WriterKeys: ImmutableArray<int>.Empty,
                    DeliveredDeltas: Enumerable.Repeat(ImmutableHashSet<int>.Empty, ReplicaCount).ToImmutableArray(),
                    CurrentWriterIdx: 0);
        }

        // ---------- Model ----------

        public sealed record ReplicaClusterModel(
            int ReplicaCount,
            ImmutableArray<ImmutableHashSet<int>> ReplicaKnownKeys,
            ImmutableArray<int> WriterKeys,
            ImmutableArray<ImmutableHashSet<int>> DeliveredDeltas,
            int CurrentWriterIdx)
        {
            public override string ToString()
            {
                var known = string.Join("; ",
                    ReplicaKnownKeys.Select((s, i) => $"R{i}=[{string.Join(",", s.OrderBy(x => x))}]"));
                return $"Model(writerKeys=[{string.Join(",", WriterKeys)}], known={{{known}}}, writer=W{CurrentWriterIdx})";
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

            public override ReplicaClusterModel Run(ReplicaClusterModel model) =>
                model with { WriterKeys = model.WriterKeys.Add(Key) };

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
                    .Zip(Gen.Choose(0, model.WriterKeys.Length - 1))
                    .Select(t => (Operation<ReplicaCluster, ReplicaClusterModel>)new DeliverDelta(t.Item1, t.Item2));

            public int ReplicaIdx { get; }
            public int DeltaIdx { get; }
            public DeliverDelta(int replicaIdx, int deltaIdx) { ReplicaIdx = replicaIdx; DeltaIdx = deltaIdx; }

            public override bool Pre(ReplicaClusterModel model) =>
                DeltaIdx >= 0 && DeltaIdx < model.WriterKeys.Length
                && !model.DeliveredDeltas[ReplicaIdx].Contains(DeltaIdx);

            public override ReplicaClusterModel Run(ReplicaClusterModel model)
            {
                var key = model.WriterKeys[DeltaIdx];
                var newKnown = model.ReplicaKnownKeys.SetItem(ReplicaIdx, model.ReplicaKnownKeys[ReplicaIdx].Add(key));
                var newDelivered = model.DeliveredDeltas.SetItem(ReplicaIdx, model.DeliveredDeltas[ReplicaIdx].Add(DeltaIdx));
                return model with { ReplicaKnownKeys = newKnown, DeliveredDeltas = newDelivered };
            }

            public override Property Check(ReplicaCluster actual, ReplicaClusterModel model)
            {
                // FsCheck.Experimental invokes Run BEFORE Check, so `model` here
                // is already the post-state.
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
                return model with { ReplicaKnownKeys = newKnown };
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
