//-----------------------------------------------------------------------
// <copyright file="PNCounterDictionary.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text;
using UniqueAddress = Akka.Cluster.UniqueAddress;

namespace Akka.DistributedData
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Marker interface for serialization
    /// </summary>
    internal interface IPNCounterDictionary
    {
        Type KeyType { get; }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// For serialization purposes.
    /// </summary>
    internal interface IPNCounterDictionaryDeltaOperation
    {
        ORDictionary.IDeltaOperation Underlying { get; }
    }

    /// <summary>
    /// Map of named counters. Specialized <see cref="ORDictionary{TKey,TValue}"/> 
    /// with <see cref="PNCounter"/> values. 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    public sealed class PNCounterDictionary<TKey> :
        IDeltaReplicatedData<PNCounterDictionary<TKey>, ORDictionary<TKey, PNCounter>.IDeltaOperation>,
        IRemovedNodePruning<PNCounterDictionary<TKey>>,
        IReplicatedDataSerialization,
        IEquatable<PNCounterDictionary<TKey>>,
        IEnumerable<KeyValuePair<TKey, BigInteger>>, IPNCounterDictionary
    {
        public static readonly PNCounterDictionary<TKey> Empty = new PNCounterDictionary<TKey>(ORDictionary<TKey, PNCounter>.Empty);

        /// <summary>
        /// Needs to be internal for serialization purposes
        /// </summary>
        internal readonly ORDictionary<TKey, PNCounter> Underlying;

        public PNCounterDictionary(ORDictionary<TKey, PNCounter> underlying)
        {
            Underlying = underlying;
        }

        /// <summary>
        /// Returns all entries stored within current <see cref="PNCounterDictionary{TKey}"/>
        /// </summary>
        public IImmutableDictionary<TKey, BigInteger> Entries => Underlying.Entries
            .Select(kv => new KeyValuePair<TKey, BigInteger>(kv.Key, kv.Value.Value))
            .ToImmutableDictionary();

        /// <summary>
        /// Returns a counter value stored within current <see cref="PNCounterDictionary{TKey}"/>
        /// under provided <paramref name="key"/>
        /// </summary>
        public BigInteger this[TKey key] => Underlying[key].Value;

        /// <summary>
        /// Determines if current <see cref="PNCounterDictionary{TKey}"/> has a counter
        /// registered under provided <paramref name="key"/>.
        /// </summary>
        public bool ContainsKey(TKey key) => Underlying.ContainsKey(key);

        /// <summary>
        /// Determines if current <see cref="PNCounterDictionary{TKey}"/> is empty.
        /// </summary>
        public bool IsEmpty => Underlying.IsEmpty;

        /// <summary>
        /// Returns number of entries stored within current <see cref="PNCounterDictionary{TKey}"/>.
        /// </summary>
        public int Count => Underlying.Count;

        /// <summary>
        /// Returns all keys of the current <see cref="PNCounterDictionary{TKey}"/>.
        /// </summary>
        public IEnumerable<TKey> Keys => Underlying.Keys;

        /// <summary>
        /// Returns all values stored within current <see cref="PNCounterDictionary{TKey}"/>.
        /// </summary>
        public IEnumerable<BigInteger> Values => Underlying.Values.Select(x => x.Value);

        /// <summary>
        /// Tries to return a value under provided <paramref name="key"/>, if such entry exists.
        /// </summary>
        public bool TryGetValue(TKey key, out BigInteger value)
        {
            if (Underlying.TryGetValue(key, out var counter))
            {
                value = counter.Value;
                return true;
            }

            value = BigInteger.Zero;
            return false;
        }

        /// <summary>
        /// Increment the counter with the delta specified.
        /// If the delta is negative then it will decrement instead of increment.
        /// </summary>
        public PNCounterDictionary<TKey> Increment(Cluster.Cluster node, TKey key, long delta = 1L) =>
            Increment(node.SelfUniqueAddress, key, delta);

        /// <summary>
        /// Increment the counter with the delta specified.
        /// If the delta is negative then it will decrement instead of increment.
        /// </summary>
        public PNCounterDictionary<TKey> Increment(UniqueAddress node, TKey key, long delta = 1L) =>
            new PNCounterDictionary<TKey>(Underlying.AddOrUpdate(node, key, PNCounter.Empty, old => old.Increment(node, delta)));

        /// <summary>
        /// Decrement the counter with the delta specified.
        /// If the delta is negative then it will increment instead of decrement.
        /// </summary>
        public PNCounterDictionary<TKey> Decrement(Cluster.Cluster node, TKey key, long delta = 1L) =>
            Decrement(node.SelfUniqueAddress, key, delta);

        /// <summary>
        /// Decrement the counter with the delta specified.
        /// If the delta is negative then it will increment instead of decrement.
        /// </summary>
        public PNCounterDictionary<TKey> Decrement(UniqueAddress node, TKey key, long delta = 1L) =>
            new PNCounterDictionary<TKey>(Underlying.AddOrUpdate(node, key, PNCounter.Empty, old => old.Decrement(node, delta)));

        /// <summary>
        /// Removes an entry from the map.
        /// Note that if there is a conflicting update on another node the entry will
        /// not be removed after merge.
        /// </summary>
        public PNCounterDictionary<TKey> Remove(Cluster.Cluster node, TKey key) =>
            Remove(node.SelfUniqueAddress, key);

        /// <summary>
        /// Removes an entry from the map.
        /// Note that if there is a conflicting update on another node the entry will
        /// not be removed after merge.
        /// </summary>
        public PNCounterDictionary<TKey> Remove(UniqueAddress node, TKey key) =>
            new PNCounterDictionary<TKey>(Underlying.Remove(node, key));

        public PNCounterDictionary<TKey> Merge(PNCounterDictionary<TKey> other) =>
            new PNCounterDictionary<TKey>(Underlying.Merge(other.Underlying));

        public IReplicatedData Merge(IReplicatedData other) =>
            Merge((PNCounterDictionary<TKey>)other);

        public ImmutableHashSet<UniqueAddress> ModifiedByNodes => Underlying.ModifiedByNodes;

        public bool NeedPruningFrom(UniqueAddress removedNode) =>
            Underlying.NeedPruningFrom(removedNode);

        IReplicatedData IRemovedNodePruning.PruningCleanup(UniqueAddress removedNode) => PruningCleanup(removedNode);

        IReplicatedData IRemovedNodePruning.Prune(UniqueAddress removedNode, UniqueAddress collapseInto) => Prune(removedNode, collapseInto);

        public PNCounterDictionary<TKey> Prune(UniqueAddress removedNode, UniqueAddress collapseInto) =>
            new PNCounterDictionary<TKey>(Underlying.Prune(removedNode, collapseInto));

        public PNCounterDictionary<TKey> PruningCleanup(UniqueAddress removedNode) =>
            new PNCounterDictionary<TKey>(Underlying.PruningCleanup(removedNode));

        /// <inheritdoc/>
        public bool Equals(PNCounterDictionary<TKey> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Underlying, other.Underlying);
        }

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<TKey, BigInteger>> GetEnumerator() =>
            Underlying.Select(x => new KeyValuePair<TKey, BigInteger>(x.Key, x.Value.Value)).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is PNCounterDictionary<TKey> pairs && Equals(pairs);

        /// <inheritdoc/>
        public override int GetHashCode() => Underlying.GetHashCode();

        /// <inheritdoc/>
        public override string ToString()
        {
            var sb = new StringBuilder("PNCounterDictionary(");
            foreach (var entry in Entries)
            {
                sb.Append(entry.Key).Append("->").Append(entry.Value).Append(", ");
            }
            sb.Append(')');
            return sb.ToString();
        }

        #region delta 

        internal sealed class PNCounterDictionaryDelta : ORDictionary<TKey, PNCounter>.IDeltaOperation, IReplicatedDeltaSize, IPNCounterDictionaryDeltaOperation
        {
            internal readonly ORDictionary<TKey, PNCounter>.IDeltaOperation Underlying;

            public PNCounterDictionaryDelta(ORDictionary<TKey, PNCounter>.IDeltaOperation underlying)
            {
                Underlying = underlying;
                if (underlying is IReplicatedDeltaSize s)
                {
                    DeltaSize = s.DeltaSize;
                }
                else
                {
                    DeltaSize = 1;
                }
            }

            public IReplicatedData Merge(IReplicatedData other)
            {
                if (other is PNCounterDictionaryDelta d)
                {
                    return new PNCounterDictionaryDelta((ORDictionary<TKey, PNCounter>.IDeltaOperation)Underlying.Merge(d.Underlying));
                }

                return new PNCounterDictionaryDelta((ORDictionary<TKey, PNCounter>.IDeltaOperation)Underlying.Merge(other));
            }

            public IDeltaReplicatedData Zero => PNCounterDictionary<TKey>.Empty;

            public override bool Equals(object obj)
            {
                return obj is PNCounterDictionary<TKey>.PNCounterDictionaryDelta operation && Equals(operation.Underlying);
            }

            public bool Equals(ORDictionary<TKey, PNCounter>.IDeltaOperation other)
            {
                if (other is ORDictionary<TKey, PNCounter>.DeltaGroup group)
                {
                    if (Underlying is ORDictionary<TKey, PNCounter>.DeltaGroup ourGroup)
                    {
                        return ourGroup.Operations.SequenceEqual(group.Operations);
                    }

                    if (group.Operations.Length == 1)
                    {
                        return Underlying.Equals(group.Operations.First());
                    }

                    return false;
                }
                return Underlying.Equals(other);
            }

            public override int GetHashCode()
            {
                return Underlying.GetHashCode();
            }

            public int DeltaSize { get; }
            ORDictionary.IDeltaOperation IPNCounterDictionaryDeltaOperation.Underlying => (ORDictionary.IDeltaOperation)Underlying;
        }

        // TODO: optimize this so it doesn't allocate each time it's called
        public ORDictionary<TKey, PNCounter>.IDeltaOperation Delta => new PNCounterDictionaryDelta(Underlying.Delta);

        public PNCounterDictionary<TKey> MergeDelta(ORDictionary<TKey, PNCounter>.IDeltaOperation delta)
        {
            switch (delta)
            {
                case PNCounterDictionaryDelta d:
                    return new PNCounterDictionary<TKey>(Underlying.MergeDelta(d.Underlying));
                default:
                    return new PNCounterDictionary<TKey>(Underlying.MergeDelta(delta));
            }
        }
            

        IReplicatedDelta IDeltaReplicatedData.Delta => Delta;

        IReplicatedData IDeltaReplicatedData.MergeDelta(IReplicatedDelta delta)
        {
            switch (delta)
            {
                case PNCounterDictionaryDelta d:
                    return MergeDelta(d.Underlying);
                default:
                    return MergeDelta((ORDictionary<TKey, PNCounter>.IDeltaOperation)delta);
            }
        }

        IReplicatedData IDeltaReplicatedData.ResetDelta() => ResetDelta();

        public PNCounterDictionary<TKey> ResetDelta() =>
            new PNCounterDictionary<TKey>(Underlying.ResetDelta());

        #endregion

        public Type KeyType { get; } = typeof(TKey);
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Marker interface for serialization
    /// </summary>
    internal interface IPNCounterDictionaryKey
    {
        Type KeyType { get; }
    }

    public class PNCounterDictionaryKey<T> : Key<PNCounterDictionary<T>>, IPNCounterDictionaryKey
    {
        public PNCounterDictionaryKey(string id) : base(id)
        {
        }

        public Type KeyType { get; } = typeof(T);
    }
}
