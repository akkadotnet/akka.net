//-----------------------------------------------------------------------
// <copyright file="GCounter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// <param name="id">The unique identifier for the counter key.</param>
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
        /// Gets the per-node counter values that make up this counter.
        /// </summary>
        public ImmutableDictionary<UniqueAddress, ulong> State { get; }

        /// <summary>
        /// Gets an empty counter whose value is zero.
        /// </summary>
        public static GCounter Empty => new();

        /// <summary>
        /// Current total value of the counter.
        /// </summary>
        public ulong Value { get; }

        [NonSerialized]
        private readonly GCounter _syncRoot; //HACK: we need to ignore this field during serialization. This is the only way to do so on Hyperion on .NET Core
        public GCounter Delta => _syncRoot;

        /// <summary>
        /// Creates an empty counter with no per-node values.
        /// </summary>
        public GCounter() : this(ImmutableDictionary<UniqueAddress, ulong>.Empty) { }

        /// <summary>
        /// Creates a counter from its per-node values and optional delta state.
        /// </summary>
        /// <param name="state">The map of node addresses to their counter values.</param>
        /// <param name="delta">The optional delta state accumulated for replication.</param>
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
        /// <param name="node">The cluster node whose component is incremented.</param>
        /// <param name="n">The non-negative amount to add to that node's component.</param>
        /// <returns>A counter whose node component is the <see cref="ulong"/> sum of its previous value and <paramref name="n"/>, which can wrap on overflow, or this instance when <paramref name="n"/> is zero.</returns>
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
        /// Merges per-node counter components by keeping the greater value for each node.
        /// </summary>
        /// <param name="other">The counter to merge with this instance.</param>
        /// <returns>A counter containing the merged per-node values.</returns>
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
        /// Returns whether this counter contains a component for the removed node that requires pruning.
        /// </summary>
        /// <param name="removedNode">The cluster node address to check.</param>
        /// <returns><see langword="true"/> if the node has a component in this counter; otherwise, <see langword="false"/>.</returns>
        public bool NeedPruningFrom(UniqueAddress removedNode) => State.ContainsKey(removedNode);

        IReplicatedData IRemovedNodePruning.PruningCleanup(UniqueAddress removedNode) => PruningCleanup(removedNode);

        IReplicatedData IRemovedNodePruning.Prune(UniqueAddress removedNode, UniqueAddress collapseInto) => Prune(removedNode, collapseInto);

        /// <summary>
        /// Moves the removed node's counter component into the component for <paramref name="collapseInto"/>.
        /// </summary>
        /// <param name="removedNode">The cluster node address whose component is pruned.</param>
        /// <param name="collapseInto">The cluster node address that receives the removed component's value.</param>
        /// <returns>A counter with the removed component transferred, or this instance if it has no component for that node.</returns>
        public GCounter Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
        {
            return State.TryGetValue(removedNode, out var prunedNodeValue)
                ? new GCounter(State.Remove(removedNode)).Increment(collapseInto, prunedNodeValue)
                : this;
        }

        /// <summary>
        /// Removes the removed node's component after pruning has been completed.
        /// </summary>
        /// <param name="removedNode">The cluster node address whose component is removed.</param>
        /// <returns>A counter without the removed node's component.</returns>
        public GCounter PruningCleanup(UniqueAddress removedNode) => new(State.Remove(removedNode));

        
        public override int GetHashCode() => State.GetHashCode();

        
        public bool Equals(GCounter other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return State.SequenceEqual(other.State);
        }

        
        public override bool Equals(object obj) => obj is GCounter counter && Equals(counter);

        
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
