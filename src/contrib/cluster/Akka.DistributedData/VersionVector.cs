// -----------------------------------------------------------------------
//  <copyright file="VersionVector.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Akka.Cluster;
using Akka.Util.Internal;

namespace Akka.DistributedData;

/// <summary>
///     Representation of a Vector-based clock (counting clock), inspired by Lamport logical clocks.
///     Based on code from <see cref="VectorClock" />.
///     This class is immutable, i.e. "modifying" methods return a new instance.
/// </summary>
[Serializable]
public abstract class VersionVector : IReplicatedData<VersionVector>, IReplicatedDataSerialization,
    IRemovedNodePruning<VersionVector>, IEquatable<VersionVector>
{
    public enum Ordering
    {
        After,
        Before,
        Same,
        Concurrent,
        FullOrder
    }

    protected static readonly AtomicCounterLong Counter = new(1L);

    /// <summary>
    ///     Marker to signal that we have reached the end of a version vector.
    /// </summary>
    private static readonly (UniqueAddress addr, long version) EndMarker = (null, long.MinValue);

    public static readonly VersionVector Empty = new MultiVersionVector(ImmutableDictionary<UniqueAddress, long>.Empty);

    public abstract bool IsEmpty { get; }

    public abstract int Count { get; }

    public abstract IEnumerator<KeyValuePair<UniqueAddress, long>> VersionEnumerator { get; }

    internal abstract IEnumerable<(UniqueAddress addr, long version)> InternalVersions { get; }

    internal IEnumerator<(UniqueAddress addr, long version)> InternalVersionEnumerator =>
        InternalVersions.GetEnumerator();


    public bool Equals(VersionVector other)
    {
        if (ReferenceEquals(other, null)) return false;
        if (ReferenceEquals(this, other)) return true;

        return CompareOnlyTo(other, Ordering.Same) == Ordering.Same;
    }

    public abstract ImmutableHashSet<UniqueAddress> ModifiedByNodes { get; }
    public abstract bool NeedPruningFrom(UniqueAddress removedNode);

    IReplicatedData IRemovedNodePruning.PruningCleanup(UniqueAddress removedNode)
    {
        return PruningCleanup(removedNode);
    }

    IReplicatedData IRemovedNodePruning.Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
    {
        return Prune(removedNode, collapseInto);
    }

    public abstract VersionVector Prune(UniqueAddress removedNode, UniqueAddress collapseInto);

    public abstract VersionVector PruningCleanup(UniqueAddress removedNode);

    /// <summary>
    ///     Merges this VersionVector with another VersionVector. E.g. merges its versioned history.
    /// </summary>
    public abstract VersionVector Merge(VersionVector other);

    public IReplicatedData Merge(IReplicatedData other)
    {
        return Merge((VersionVector)other);
    }

    public static VersionVector Create(UniqueAddress node, long version)
    {
        return new SingleVersionVector(node, version);
    }

    public static VersionVector Create(ImmutableDictionary<UniqueAddress, long> versions)
    {
        if (versions.IsEmpty) return Empty;
        if (versions.Count == 1)
        {
            var v = versions.First();
            return new SingleVersionVector(v.Key, v.Value);
        }

        return new MultiVersionVector(versions);
    }

    /// <summary>
    ///     Increment the version for the node passed as argument. Returns a new VersionVector.
    /// </summary>
    public abstract VersionVector Increment(UniqueAddress node);

    public abstract long VersionAt(UniqueAddress node);

    public abstract bool Contains(UniqueAddress node);


    public override bool Equals(object obj)
    {
        return obj is VersionVector vector && Equals(vector);
    }

    /// <summary>
    ///     Returns true if this VersionVector has the same history
    ///     as the <paramref name="y" /> VersionVector else false.
    /// </summary>
    public bool IsSame(VersionVector y)
    {
        return CompareOnlyTo(y, Ordering.Same) == Ordering.Same;
    }

    public bool IsConcurrent(VersionVector y)
    {
        return CompareOnlyTo(y, Ordering.Concurrent) == Ordering.Concurrent;
    }

    public bool IsBefore(VersionVector y)
    {
        return CompareOnlyTo(y, Ordering.Before) == Ordering.Before;
    }

    public bool IsAfter(VersionVector y)
    {
        return CompareOnlyTo(y, Ordering.After) == Ordering.After;
    }

    public static bool operator ==(VersionVector x, VersionVector y)
    {
        return x?.Equals(y) ?? ReferenceEquals(x, y);
    }

    /// <summary>
    ///     Returns true if <paramref name="x" /> VersionVector has other
    ///     history than the <paramref name="y" /> VersionVector else false.
    /// </summary>
    public static bool operator !=(VersionVector x, VersionVector y)
    {
        return !(x == y);
    }

    /// <summary>
    ///     Returns true if <paramref name="x" /> is after <paramref name="y" /> else false.
    /// </summary>
    public static bool operator >(VersionVector x, VersionVector y)
    {
        return x.CompareOnlyTo(y, Ordering.After) == Ordering.After;
    }

    /// <summary>
    ///     Returns true if <paramref name="x" /> is before <paramref name="y" /> else false.
    /// </summary>
    public static bool operator <(VersionVector x, VersionVector y)
    {
        return x.CompareOnlyTo(y, Ordering.Before) == Ordering.Before;
    }

    /// <summary>
    ///     Compare two version vectors. The outcome will be one of the following:
    ///     <para>Version 1 is SAME (==)       as Version 2 iff for all i c1(i) == c2(i)</para>
    ///     <para>
    ///         Version 1 is BEFORE (&lt;)      Version 2 iff for all i c1(i) &lt;= c2(i) and there exist a j such that c1(j)
    ///         &lt; c2(j)
    ///     </para>
    ///     <para>
    ///         Version 1 is AFTER (&gt;)       Version 2 iff for all i c1(i) &gt;= c2(i) and there exist a j such that c1(j)
    ///         &gt; c2(j)
    ///     </para>
    ///     <para>Version 1 is CONCURRENT to Version 2 otherwise</para>
    /// </summary>
    public Ordering Compare(VersionVector other)
    {
        return CompareOnlyTo(other, Ordering.FullOrder);
    }

    /// <summary>
    ///     Version vector comparison according to the semantics described by compareTo, with the ability to bail
    ///     out early if the we can't reach the Ordering that we are looking for.
    ///     The ordering always starts with <see cref="Ordering.Same" /> and can then go to Same, Before or After
    ///     If we're on <see cref="Ordering.After" /> we can only go to After or Concurrent
    ///     If we're on <see cref="Ordering.Before" /> we can only go to Before or Concurrent
    ///     If we go to <see cref="Ordering.Concurrent" /> we exit the loop immediately
    ///     If you send in the ordering <see cref="Ordering.FullOrder" />, you will get a full comparison.
    /// </summary>
    private Ordering CompareOnlyTo(VersionVector other, Ordering order)
    {
        if (ReferenceEquals(this, other)) return Ordering.Same;

        return Compare(InternalVersionEnumerator, other.InternalVersionEnumerator,
            order == Ordering.Concurrent ? Ordering.FullOrder : order);
    }

    private static T NextOrElse<T>(IEnumerator<T> enumerator, T defaultValue)
    {
        return enumerator.MoveNext() ? enumerator.Current : defaultValue;
    }

    private Ordering Compare(IEnumerator<(UniqueAddress addr, long version)> i1,
        IEnumerator<(UniqueAddress addr, long version)> i2, Ordering requestedOrder)
    {
        var nt1 = NextOrElse(i1, EndMarker);
        var nt2 = NextOrElse(i2, EndMarker);
        var currentOrder = Ordering.Same;
        while (true)
            if (requestedOrder != Ordering.FullOrder && currentOrder != Ordering.Same && currentOrder != requestedOrder)
            {
                return currentOrder;
            }
            else if (Equals(nt1, EndMarker) && Equals(nt2, EndMarker))
            {
                return currentOrder;
            }
            else if (Equals(nt1, EndMarker))
            {
                return currentOrder == Ordering.After ? Ordering.Concurrent : Ordering.Before;
            }
            else if (Equals(nt2, EndMarker))
            {
                return currentOrder == Ordering.Before ? Ordering.Concurrent : Ordering.After;
            }
            else
            {
                var nc = nt1.addr.CompareTo(nt2.addr);
                if (nc == 0)
                {
                    if (nt1.version < nt2.version)
                    {
                        if (currentOrder == Ordering.After) return Ordering.Concurrent;
                        currentOrder = Ordering.Before;
                    }
                    else if (nt1.version > nt2.version)
                    {
                        if (currentOrder == Ordering.Before) return Ordering.Concurrent;
                        currentOrder = Ordering.After;
                    }

                    nt1 = NextOrElse(i1, EndMarker);
                    nt2 = NextOrElse(i2, EndMarker);
                }
                else if (nc < 0)
                {
                    if (currentOrder == Ordering.Before) return Ordering.Concurrent;
                    currentOrder = Ordering.After;
                    nt1 = NextOrElse(i1, EndMarker);
                }
                else
                {
                    if (currentOrder == Ordering.After) return Ordering.Concurrent;
                    currentOrder = Ordering.Before;
                    nt2 = NextOrElse(i2, EndMarker);
                }
            }
    }
}

[DebuggerDisplay("VersionVector({Node}->{Version})")]
public sealed class SingleVersionVector : VersionVector
{
    internal readonly UniqueAddress Node;
    internal readonly long Version;

    public SingleVersionVector(UniqueAddress node, long version)
    {
        Node = node;
        Version = version;
    }

    public override bool IsEmpty => false;
    public override int Count => 1;
    public override IEnumerator<KeyValuePair<UniqueAddress, long>> VersionEnumerator => new Enumerator(Node, Version);

    internal override IEnumerable<(UniqueAddress addr, long version)> InternalVersions
    {
        get { yield return (Node, Version); }
    }

    public override ImmutableHashSet<UniqueAddress> ModifiedByNodes => ImmutableHashSet.Create(Node);

    public override VersionVector Increment(UniqueAddress node)
    {
        var v = Counter.GetAndIncrement();
        return node == Node
            ? new SingleVersionVector(Node, v)
            : new MultiVersionVector(
                new KeyValuePair<UniqueAddress, long>(Node, Version),
                new KeyValuePair<UniqueAddress, long>(node, v));
    }

    public override long VersionAt(UniqueAddress node)
    {
        return node == Node ? Version : 0L;
    }

    public override bool Contains(UniqueAddress node)
    {
        return Node == node;
    }

    public override VersionVector Merge(VersionVector other)
    {
        switch (other)
        {
            case MultiVersionVector vector1:
            {
                var v2 = vector1.Versions.GetValueOrDefault(Node, 0L);
                var mergedVersions = v2 >= Version ? vector1.Versions : vector1.Versions.SetItem(Node, Version);
                return new MultiVersionVector(mergedVersions);
            }
            case SingleVersionVector vector when Node == vector.Node:
                return Version >= vector.Version ? this : new SingleVersionVector(vector.Node, vector.Version);
            case SingleVersionVector vector:
                return new MultiVersionVector(
                    new KeyValuePair<UniqueAddress, long>(Node, Version),
                    new KeyValuePair<UniqueAddress, long>(vector.Node, vector.Version));
            default:
                throw new NotSupportedException(
                    "SingleVersionVector doesn't support merge with provided version vector");
        }
    }

    public override bool NeedPruningFrom(UniqueAddress removedNode)
    {
        return Node == removedNode;
    }

    public override VersionVector Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
    {
        return (Node == removedNode ? Empty : this).Increment(collapseInto);
    }

    public override VersionVector PruningCleanup(UniqueAddress removedNode)
    {
        return Node == removedNode ? Empty : this;
    }

    public override string ToString()
    {
        return $"VersionVector({Node}->{Version})";
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (int)(Node.GetHashCode() ^ Version);
        }
    }

    private sealed class Enumerator : IEnumerator<KeyValuePair<UniqueAddress, long>>
    {
        private bool _moved;

        public Enumerator(UniqueAddress node, long version)
        {
            Current = new KeyValuePair<UniqueAddress, long>(node, version);
        }


        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            if (!_moved)
            {
                _moved = true;
                return true;
            }

            return false;
        }

        public void Reset()
        {
            _moved = false;
        }

        public KeyValuePair<UniqueAddress, long> Current { get; }

        object IEnumerator.Current => Current;
    }
}

[Serializable]
public sealed class MultiVersionVector : VersionVector
{
    internal readonly ImmutableDictionary<UniqueAddress, long> Versions;

    public MultiVersionVector(params KeyValuePair<UniqueAddress, long>[] nodeVersions)
    {
        Versions = nodeVersions.ToImmutableDictionary();
    }

    public MultiVersionVector(IEnumerable<KeyValuePair<UniqueAddress, long>> versions)
    {
        Versions = versions.ToImmutableDictionary();
    }

    public MultiVersionVector(ImmutableDictionary<UniqueAddress, long> nodeVersions)
    {
        Versions = nodeVersions;
    }

    public override bool IsEmpty => Versions.IsEmpty;
    public override int Count => Versions.Count;
    public override IEnumerator<KeyValuePair<UniqueAddress, long>> VersionEnumerator => Versions.GetEnumerator();

    internal override IEnumerable<(UniqueAddress addr, long version)> InternalVersions =>
        Versions.Select(x => (x.Key, x.Value));

    public override ImmutableHashSet<UniqueAddress> ModifiedByNodes => Versions.Keys.ToImmutableHashSet();

    public override VersionVector Increment(UniqueAddress node)
    {
        return new MultiVersionVector(Versions.SetItem(node, Counter.GetAndIncrement()));
    }

    public override long VersionAt(UniqueAddress node)
    {
        return Versions.GetValueOrDefault(node, 0L);
    }

    public override bool Contains(UniqueAddress node)
    {
        return Versions.ContainsKey(node);
    }

    public override VersionVector Merge(VersionVector other)
    {
        switch (other)
        {
            case MultiVersionVector vector1:
            {
                var merged = vector1.Versions.ToBuilder();
                foreach (var pair in Versions)
                {
                    var mergedCurrentTime = merged.GetValueOrDefault(pair.Key, 0L);
                    if (pair.Value >= mergedCurrentTime)
                        merged[pair.Key] = pair.Value;
                }

                return new MultiVersionVector(merged.ToImmutable());
            }
            case SingleVersionVector vector:
            {
                var v1 = Versions.GetValueOrDefault(vector.Node, 0L);
                var merged = v1 >= vector.Version ? Versions : Versions.SetItem(vector.Node, vector.Version);
                return new MultiVersionVector(merged);
            }
            default:
                throw new NotSupportedException(
                    "MultiVersionVector doesn't support merge with provided version vector");
        }
    }

    public override bool NeedPruningFrom(UniqueAddress removedNode)
    {
        return Versions.ContainsKey(removedNode);
    }

    public override VersionVector Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
    {
        return new MultiVersionVector(Versions.Remove(removedNode)).Increment(collapseInto);
    }

    public override VersionVector PruningCleanup(UniqueAddress removedNode)
    {
        return new MultiVersionVector(Versions.Remove(removedNode));
    }


    public override string ToString()
    {
        return $"VersionVector({string.Join(";", Versions.Select(kv => $"({kv.Key}->{kv.Value})"))})";
    }


    public override int GetHashCode()
    {
        unchecked
        {
            var seed = 17;
            foreach (var v in Versions) seed *= (int)(v.Key.GetHashCode() ^ v.Value);

            return seed;
        }
    }
}