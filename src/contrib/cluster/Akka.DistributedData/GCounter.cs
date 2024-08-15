// -----------------------------------------------------------------------
//  <copyright file="GCounter.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster;

namespace Akka.DistributedData;

/// <summary>
///     A typed key for <see cref="GCounter" /> CRDT. Can be used to perform read/upsert/delete
///     operations on correlated data type.
/// </summary>
[Serializable]
public sealed class GCounterKey : Key<GCounter>
{
    /// <summary>
    ///     Creates a new instance of <see cref="GCounterKey" /> class.
    /// </summary>
    /// <param name="id">TBD</param>
    public GCounterKey(string id) : base(id)
    {
    }
}

/// <summary>
///     Implements a 'Growing Counter' CRDT, also called a 'G-Counter'.
///     It is described in the paper
///     <a href="http://hal.upmc.fr/file/index/docid/555588/filename/techreport.pdf">
///         A comprehensive study of Convergent
///         and Commutative Replicated Data Types
///     </a>
///     .
///     A G-Counter is a increment-only counter (inspired by vector clocks) in
///     which only increment and merge are possible. Incrementing the counter
///     adds 1 to the count for the current node. Divergent histories are
///     resolved by taking the maximum count for each node (like a vector
///     clock merge). The value of the counter is the sum of all node counts.
///     This class is immutable, i.e. "modifying" methods return a new instance.
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

    [NonSerialized]
    private readonly GCounter
        _syncRoot; //HACK: we need to ignore this field during serialization. This is the only way to do so on Hyperion on .NET Core

    /// <summary>
    ///     TBD
    /// </summary>
    public GCounter() : this(ImmutableDictionary<UniqueAddress, ulong>.Empty)
    {
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="state">TBD</param>
    /// <param name="delta">TBD</param>
    internal GCounter(ImmutableDictionary<UniqueAddress, ulong> state, GCounter delta = null)
    {
        _syncRoot = delta;
        State = state;
        Value = State.Aggregate(Zero, (v, acc) => v + acc.Value);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    public ImmutableDictionary<UniqueAddress, ulong> State { get; }

    /// <summary>
    ///     TBD
    /// </summary>
    public static GCounter Empty => new();

    /// <summary>
    ///     Current total value of the counter.
    /// </summary>
    public ulong Value { get; }

    public GCounter Delta => _syncRoot;

    public GCounter MergeDelta(GCounter delta)
    {
        return Merge(delta);
    }

    IReplicatedDelta IDeltaReplicatedData.Delta => Delta;

    IReplicatedData IDeltaReplicatedData.MergeDelta(IReplicatedDelta delta)
    {
        return MergeDelta((GCounter)delta);
    }

    IReplicatedData IDeltaReplicatedData.ResetDelta()
    {
        return ResetDelta();
    }

    public GCounter ResetDelta()
    {
        return Delta == null ? this : AssignAncestor(new GCounter(State));
    }


    public bool Equals(GCounter other)
    {
        if (ReferenceEquals(other, null)) return false;
        if (ReferenceEquals(this, other)) return true;

        return State.SequenceEqual(other.State);
    }

    public ImmutableHashSet<UniqueAddress> ModifiedByNodes => State.Keys.ToImmutableHashSet();

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="other">TBD</param>
    /// <returns>TBD</returns>
    public override GCounter Merge(GCounter other)
    {
        if (ReferenceEquals(this, other) || other.IsAncestorOf(this)) return ClearAncestor();

        if (IsAncestorOf(other))
        {
            return other.ClearAncestor();
        }

        var merged = other.State;
        foreach (var kvp in State)
        {
            var otherValue = merged.GetValueOrDefault(kvp.Key, Zero);
            if (kvp.Value > otherValue) merged = merged.SetItem(kvp.Key, kvp.Value);
        }

        ClearAncestor();
        return new GCounter(merged);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="removedNode">TBD</param>
    /// <returns>TBD</returns>
    public bool NeedPruningFrom(UniqueAddress removedNode)
    {
        return State.ContainsKey(removedNode);
    }

    IReplicatedData IRemovedNodePruning.PruningCleanup(UniqueAddress removedNode)
    {
        return PruningCleanup(removedNode);
    }

    IReplicatedData IRemovedNodePruning.Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
    {
        return Prune(removedNode, collapseInto);
    }

    /// <summary>
    ///     TBD
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
    ///     TBD
    /// </summary>
    /// <param name="removedNode">TBD</param>
    /// <returns>TBD</returns>
    public GCounter PruningCleanup(UniqueAddress removedNode)
    {
        return new GCounter(State.Remove(removedNode));
    }

    IDeltaReplicatedData IReplicatedDelta.Zero => Empty;

    /// <summary>
    ///     Increment the counter with the delta specified. The delta must be zero or positive.
    /// </summary>
    public GCounter Increment(Cluster.Cluster node, ulong delta = 1)
    {
        return Increment(node.SelfUniqueAddress, delta);
    }

    /// <summary>
    ///     Increment the counter with the delta specified. The delta must be zero or positive.
    /// </summary>
    /// <param name="node">TBD</param>
    /// <param name="n">TBD</param>
    /// <exception cref="ArgumentException">
    ///     This exception is thrown when the specified <paramref name="n" /> is less than zero.
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


    public override int GetHashCode()
    {
        return State.GetHashCode();
    }


    public override bool Equals(object obj)
    {
        return obj is GCounter counter && Equals(counter);
    }


    public override string ToString()
    {
        return $"GCounter({Value})";
    }

    /// <summary>
    ///     Performs an implicit conversion from <see cref="GCounter" /> to <see cref="ulong" />.
    /// </summary>
    /// <param name="counter">The counter to convert</param>
    /// <returns>The result of the conversion</returns>
    public static implicit operator ulong(GCounter counter)
    {
        return counter.Value;
    }
}