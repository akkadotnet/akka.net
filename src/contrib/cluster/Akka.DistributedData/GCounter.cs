//-----------------------------------------------------------------------
// <copyright file="GCounter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;

namespace Akka.DistributedData
{
    /// <summary>
    /// A typed key for <see cref="GCounter"/> CRDT. Can be used to perform read/upsert/delete
    /// operations on correlated data type.
    /// </summary>
    [Serializable]
    public sealed class GCounterKey : Key<GCounter>
    {
        /// <summary>
        /// Creates a new instance of <see cref="GCounterKey"/> class.
        /// </summary>
        /// <param name="id">TBD</param>
        public GCounterKey(string id) : base(id) { }
    }

    /// <summary>
    /// Implements a 'Growing Counter' CRDT, also called a 'G-Counter'.
    /// 
    /// It is described in the paper
    /// <a href="http://hal.upmc.fr/file/index/docid/555588/filename/techreport.pdf">A comprehensive study of Convergent and Commutative Replicated Data Types</a>.
    /// 
    /// A G-Counter is a increment-only counter (inspired by vector clocks) in
    /// which only increment and merge are possible. Incrementing the counter
    /// adds 1 to the count for the current node. Divergent histories are
    /// resolved by taking the maximum count for each node (like a vector
    /// clock merge). The value of the counter is the sum of all node counts.
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    [Serializable]
    public sealed class GCounter :
        FastMerge<GCounter>,
        IRemovedNodePruning<GCounter>,
        IEquatable<GCounter>,
        IReplicatedDataSerialization,
        IDeltaReplicatedData<GCounter, GCounter>,
        IReplicatedDelta
    {
        private static readonly ulong Zero = 0UL;

        /// <summary>
        /// TBD
        /// </summary>
        public ImmutableDictionary<UniqueAddress, ulong> State { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public static GCounter Empty => new GCounter();

        /// <summary>
        /// Current total value of the counter.
        /// </summary>
        public ulong Value { get; }

        [NonSerialized]
        private readonly GCounter _syncRoot; //HACK: we need to ignore this field during serialization. This is the only way to do so on Hyperion on .NET Core
        public GCounter Delta => _syncRoot;

        /// <summary>
        /// TBD
        /// </summary>
        public GCounter() : this(ImmutableDictionary<UniqueAddress, ulong>.Empty) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="state">TBD</param>
        /// <param name="delta">TBD</param>
        internal GCounter(ImmutableDictionary<UniqueAddress, ulong> state, GCounter delta = null)
        {
            _syncRoot = delta;
            State = state;
            Value = State.Aggregate(Zero, (v, acc) => v + acc.Value);
        }

        public ImmutableHashSet<UniqueAddress> ModifiedByNodes => State.Keys.ToImmutableHashSet();
        
        /// <summary>
        /// Increment the counter with the delta specified. The delta must be zero or positive.
        /// </summary>
        public GCounter Increment(Cluster.Cluster node, ulong delta = 1) => Increment(node.SelfUniqueAddress, delta);
        
        /// <summary>
        /// Increment the counter with the delta specified. The delta must be zero or positive.
        /// </summary>
        /// <param name="node">TBD</param>
        /// <param name="n">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="n"/> is less than zero.
        /// </exception>
        /// <returns>TBD</returns>
        public GCounter Increment(UniqueAddress node, ulong n = 1)
        {
            if (n == 0) return this;

            var nextValue = State.GetValueOrDefault(node, 0UL) + n;
            var newDelta = Delta == null
                ? new GCounter(
                    ImmutableDictionary.CreateRange(new[] { new KeyValuePair<UniqueAddress, ulong>(node, nextValue) }))
                : new GCounter(Delta.State.SetItem(node, nextValue));

            return AssignAncestor(new GCounter(State.SetItem(node, nextValue), newDelta));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public override GCounter Merge(GCounter other)
        {
            if (ReferenceEquals(this, other) || other.IsAncestorOf(this)) return ClearAncestor();
            else if (IsAncestorOf(other)) return other.ClearAncestor();
            else
            {
                var merged = other.State;
                foreach (var kvp in State)
                {
                    var otherValue = merged.GetValueOrDefault(kvp.Key, Zero);
                    if (kvp.Value > otherValue)
                    {
                        merged = merged.SetItem(kvp.Key, kvp.Value);
                    }
                }
                ClearAncestor();
                return new GCounter(merged);
            }
        }

        public GCounter MergeDelta(GCounter delta) => Merge(delta);
        
        IReplicatedDelta IDeltaReplicatedData.Delta => Delta;

        IReplicatedData IDeltaReplicatedData.MergeDelta(IReplicatedDelta delta) => MergeDelta((GCounter)delta);
        IReplicatedData IDeltaReplicatedData.ResetDelta() => ResetDelta();

        public GCounter ResetDelta() => Delta == null ? this : AssignAncestor(new GCounter(State));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedNode">TBD</param>
        /// <returns>TBD</returns>
        public bool NeedPruningFrom(UniqueAddress removedNode) => State.ContainsKey(removedNode);

        IReplicatedData IRemovedNodePruning.PruningCleanup(UniqueAddress removedNode) => PruningCleanup(removedNode);

        IReplicatedData IRemovedNodePruning.Prune(UniqueAddress removedNode, UniqueAddress collapseInto) => Prune(removedNode, collapseInto);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedNode">TBD</param>
        /// <param name="collapseInto">TBD</param>
        /// <returns>TBD</returns>
        public GCounter Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
        {
            return State.TryGetValue(removedNode, out var prunedNodeValue)
                ? new GCounter(State.Remove(removedNode)).Increment(collapseInto, prunedNodeValue)
                : this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedNode">TBD</param>
        /// <returns>TBD</returns>
        public GCounter PruningCleanup(UniqueAddress removedNode) => new GCounter(State.Remove(removedNode));

        /// <inheritdoc/>
        public override int GetHashCode() => State.GetHashCode();

        /// <inheritdoc/>
        public bool Equals(GCounter other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return State.SequenceEqual(other.State);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is GCounter counter && Equals(counter);

        /// <inheritdoc/>
        public override string ToString() => $"GCounter({Value})";

        /// <summary>
        /// Performs an implicit conversion from <see cref="GCounter" /> to <see cref="ulong" />.
        /// </summary>
        /// <param name="counter">The counter to convert</param>
        /// <returns>The result of the conversion</returns>
        public static implicit operator ulong(GCounter counter) => counter.Value;

        IDeltaReplicatedData IReplicatedDelta.Zero => GCounter.Empty;
    }
}
