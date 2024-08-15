// -----------------------------------------------------------------------
//  <copyright file="PNCounter.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Numerics;
using Akka.Cluster;

namespace Akka.DistributedData;

/// <summary>
///     Implements a 'Increment/Decrement Counter' CRDT, also called a 'PN-Counter'.
///     It is described in the paper
///     <a href="http://hal.upmc.fr/file/index/docid/555588/filename/techreport.pdf">
///         A comprehensive study of Convergent
///         and Commutative Replicated Data Types
///     </a>
///     .
///     PN-Counters allow the counter to be incremented by tracking the
///     increments (P) separate from the decrements (N). Both P and N are represented
///     as two internal [[GCounter]]s. Merge is handled by merging the internal P and N
///     counters. The value of the counter is the value of the P _counter minus
///     the value of the N counter.
///     This class is immutable, i.e. "modifying" methods return a new instance.
/// </summary>
[Serializable]
public sealed class PNCounter :
    IDeltaReplicatedData<PNCounter, PNCounter>,
    IRemovedNodePruning<PNCounter>,
    IReplicatedDataSerialization,
    IEquatable<PNCounter>,
    IReplicatedDelta
{
    public static readonly PNCounter Empty = new();

    public PNCounter() : this(GCounter.Empty, GCounter.Empty)
    {
    }

    public PNCounter(GCounter increments, GCounter decrements)
    {
        Increments = increments;
        Decrements = decrements;
    }

    public BigInteger Value => new BigInteger(Increments.Value) - new BigInteger(Decrements.Value);

    public GCounter Increments { get; }

    public GCounter Decrements { get; }

    public PNCounter Merge(PNCounter other)
    {
        return new PNCounter(Increments.Merge(other.Increments), Decrements.Merge(other.Decrements));
    }

    public IReplicatedData Merge(IReplicatedData other)
    {
        return Merge((PNCounter)other);
    }

    IReplicatedDelta IDeltaReplicatedData.Delta => Delta;

    IReplicatedData IDeltaReplicatedData.MergeDelta(IReplicatedDelta delta)
    {
        return Merge((PNCounter)delta);
    }

    IReplicatedData IDeltaReplicatedData.ResetDelta()
    {
        return ResetDelta();
    }


    public bool Equals(PNCounter other)
    {
        if (ReferenceEquals(other, null)) return false;
        if (ReferenceEquals(this, other)) return true;

        return other.Increments.Equals(Increments) && other.Decrements.Equals(Decrements);
    }

    public ImmutableHashSet<UniqueAddress> ModifiedByNodes =>
        Increments.ModifiedByNodes.Union(Decrements.ModifiedByNodes);

    public bool NeedPruningFrom(UniqueAddress removedNode)
    {
        return Increments.NeedPruningFrom(removedNode) || Decrements.NeedPruningFrom(removedNode);
    }

    IReplicatedData IRemovedNodePruning.PruningCleanup(UniqueAddress removedNode)
    {
        return PruningCleanup(removedNode);
    }

    IReplicatedData IRemovedNodePruning.Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
    {
        return Prune(removedNode, collapseInto);
    }

    public PNCounter Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
    {
        return new PNCounter(Increments.Prune(removedNode, collapseInto), Decrements.Prune(removedNode, collapseInto));
    }

    public PNCounter PruningCleanup(UniqueAddress removedNode)
    {
        return new PNCounter(Increments.PruningCleanup(removedNode), Decrements.PruningCleanup(removedNode));
    }

    IDeltaReplicatedData IReplicatedDelta.Zero => Empty;

    /// <summary>
    ///     Increment the counter with the delta specified.
    ///     If the delta is negative then it will decrement instead of increment.
    /// </summary>
    public PNCounter Increment(Cluster.Cluster node, long delta = 1)
    {
        return Increment(node.SelfUniqueAddress, delta);
    }

    /// <summary>
    ///     Increment the counter with the delta specified.
    ///     If the delta is negative then it will decrement instead of increment.
    /// </summary>
    public PNCounter Increment(UniqueAddress address, long delta = 1)
    {
        return Change(address, delta);
    }

    /// <summary>
    ///     Decrement the counter with the delta specified.
    ///     If the delta is negative then it will increment instead of decrement.
    /// </summary>
    public PNCounter Decrement(Cluster.Cluster node, long delta = 1)
    {
        return Decrement(node.SelfUniqueAddress, delta);
    }

    /// <summary>
    ///     Decrement the counter with the delta specified.
    ///     If the delta is negative then it will increment instead of decrement.
    /// </summary>
    public PNCounter Decrement(UniqueAddress address, long delta = 1)
    {
        return Change(address, -delta);
    }

    private PNCounter Change(UniqueAddress key, long delta)
    {
        if (delta > 0) return new PNCounter(Increments.Increment(key, (ulong)delta), Decrements);
        if (delta < 0) return new PNCounter(Increments, Decrements.Increment(key, (ulong)-delta));

        return this;
    }


    public override string ToString()
    {
        return $"PNCounter({Value})";
    }


    public override bool Equals(object obj)
    {
        return obj is PNCounter counter && Equals(counter);
    }


    public override int GetHashCode()
    {
        return Increments.GetHashCode() ^ Decrements.GetHashCode();
    }

    #region delta

    public PNCounter Delta => new(Increments.Delta ?? GCounter.Empty, Decrements.Delta ?? GCounter.Empty);

    public PNCounter MergeDelta(PNCounter delta)
    {
        return Merge(delta);
    }

    public PNCounter ResetDelta()
    {
        if (Increments.Delta == null && Decrements.Delta == null)
            return this;

        return new PNCounter(Increments.ResetDelta(), Decrements.ResetDelta());
    }

    #endregion
}

public sealed class PNCounterKey : Key<PNCounter>
{
    public PNCounterKey(string id)
        : base(id)
    {
    }
}