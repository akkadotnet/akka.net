//-----------------------------------------------------------------------
// <copyright file="PNCounter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster;
using System;
using System.Collections.Immutable;
using System.Numerics;

namespace Akka.DistributedData
{
    /// <summary>
    /// Implements a 'Increment/Decrement Counter' CRDT, also called a 'PN-Counter'.
    /// 
    /// It is described in the paper
    /// <a href="http://hal.upmc.fr/file/index/docid/555588/filename/techreport.pdf">A comprehensive study of Convergent and Commutative Replicated Data Types</a>.
    /// 
    /// PN-Counters allow the counter to be incremented by tracking the
    /// increments (P) separate from the decrements (N). Both P and N are represented
    /// as two internal [[GCounter]]s. Merge is handled by merging the internal P and N
    /// counters. The value of the counter is the value of the P _counter minus
    /// the value of the N counter.
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    [Serializable]
    public sealed class PNCounter :
        IDeltaReplicatedData<PNCounter, PNCounter>,
        IRemovedNodePruning<PNCounter>,
        IReplicatedDataSerialization,
        IEquatable<PNCounter>,
        IReplicatedDelta
    {
        public static readonly PNCounter Empty = new PNCounter();

        public BigInteger Value => new BigInteger(Increments.Value) - new BigInteger(Decrements.Value);

        public GCounter Increments { get; }

        public GCounter Decrements { get; }

        public PNCounter() : this(GCounter.Empty, GCounter.Empty) { }

        public PNCounter(GCounter increments, GCounter decrements)
        {
            Increments = increments;
            Decrements = decrements;
        }

        /// <summary>
        /// Increment the counter with the delta specified.
        /// If the delta is negative then it will decrement instead of increment.
        /// </summary>
        public PNCounter Increment(Cluster.Cluster node, long delta = 1) => Increment(node.SelfUniqueAddress, delta);

        /// <summary>
        /// Increment the counter with the delta specified.
        /// If the delta is negative then it will decrement instead of increment.
        /// </summary>
        public PNCounter Increment(UniqueAddress address, long delta = 1) => Change(address, delta);

        /// <summary>
        /// Decrement the counter with the delta specified.
        /// If the delta is negative then it will increment instead of decrement.
        /// </summary>
        public PNCounter Decrement(Cluster.Cluster node, long delta = 1) => Decrement(node.SelfUniqueAddress, delta);

        /// <summary>
        /// Decrement the counter with the delta specified.
        /// If the delta is negative then it will increment instead of decrement.
        /// </summary>
        public PNCounter Decrement(UniqueAddress address, long delta = 1) => Change(address, -delta);

        private PNCounter Change(UniqueAddress key, long delta)
        {
            if (delta > 0) return new PNCounter(Increments.Increment(key, (ulong)delta), Decrements);
            if (delta < 0) return new PNCounter(Increments, Decrements.Increment(key, (ulong)(-delta)));

            return this;
        }

        public PNCounter Merge(PNCounter other) =>
            new PNCounter(Increments.Merge(other.Increments), Decrements.Merge(other.Decrements));

        public ImmutableHashSet<UniqueAddress> ModifiedByNodes => Increments.ModifiedByNodes.Union(Decrements.ModifiedByNodes);

        public bool NeedPruningFrom(Cluster.UniqueAddress removedNode) =>
            Increments.NeedPruningFrom(removedNode) || Decrements.NeedPruningFrom(removedNode);

        IReplicatedData IRemovedNodePruning.PruningCleanup(UniqueAddress removedNode) => PruningCleanup(removedNode);

        IReplicatedData IRemovedNodePruning.Prune(UniqueAddress removedNode, UniqueAddress collapseInto) => Prune(removedNode, collapseInto);

        public PNCounter Prune(Cluster.UniqueAddress removedNode, Cluster.UniqueAddress collapseInto) =>
            new PNCounter(Increments.Prune(removedNode, collapseInto), Decrements.Prune(removedNode, collapseInto));

        public PNCounter PruningCleanup(Cluster.UniqueAddress removedNode) =>
            new PNCounter(Increments.PruningCleanup(removedNode), Decrements.PruningCleanup(removedNode));

        /// <inheritdoc/>
        public bool Equals(PNCounter other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return other.Increments.Equals(Increments) && other.Decrements.Equals(Decrements);
        }

        /// <inheritdoc/>
        public override string ToString() => $"PNCounter({Value})";

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is PNCounter && Equals((PNCounter)obj);

        /// <inheritdoc/>
        public override int GetHashCode() => Increments.GetHashCode() ^ Decrements.GetHashCode();

        public IReplicatedData Merge(IReplicatedData other) => Merge((PNCounter)other);
        IReplicatedDelta IDeltaReplicatedData.Delta => Delta;

        IReplicatedData IDeltaReplicatedData.MergeDelta(IReplicatedDelta delta) => Merge((PNCounter)delta);
        IReplicatedData IDeltaReplicatedData.ResetDelta() => ResetDelta();

        #region delta

        public PNCounter Delta => new PNCounter(Increments.Delta ?? GCounter.Empty, Decrements.Delta ?? GCounter.Empty);

        public PNCounter MergeDelta(PNCounter delta) => Merge(delta);

        public PNCounter ResetDelta()
        {
            if (Increments.Delta == null && Decrements.Delta == null)
                return this;

            return new PNCounter(Increments.ResetDelta(), Decrements.ResetDelta());
        }

        #endregion

        IDeltaReplicatedData IReplicatedDelta.Zero => PNCounter.Empty;
    }

    public sealed class PNCounterKey : Key<PNCounter>
    {
        public PNCounterKey(string id)
            : base(id)
        { }
    }
}
