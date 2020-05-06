//-----------------------------------------------------------------------
// <copyright file="ORMultiValueDictionary.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Cluster;

namespace Akka.DistributedData
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Marker interface for serialization
    /// </summary>
    internal interface IORMultiValueDictionaryKey
    {
        Type KeyType { get; }

        Type ValueType { get; }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// For serialization purposes.
    /// </summary>
    internal interface IORMultiValueDictionaryDeltaOperation
    {
        bool WithValueDeltas { get; }
        ORDictionary.IDeltaOperation Underlying { get; }
    }

    [Serializable]
    public sealed class ORMultiValueDictionaryKey<TKey, TValue> : Key<ORMultiValueDictionary<TKey, TValue>>, IORMultiValueDictionaryKey
    {
        public ORMultiValueDictionaryKey(string id) : base(id)
        {
        }

        public Type KeyType { get; } = typeof(TKey);
        public Type ValueType { get; } = typeof(TValue);
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Marker interface for serialization
    /// </summary>
    internal interface IORMultiValueDictionary
    {
        Type KeyType { get; }

        Type ValueType { get; }
    }

    /// <summary>
    /// An immutable multi-map implementation. This class wraps an
    /// <see cref="ORDictionary{TKey,TValue}"/> with an <see cref="ORSet{T}"/> for the map's value.
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    [Serializable]
    public sealed class ORMultiValueDictionary<TKey, TValue> :
        IDeltaReplicatedData<ORMultiValueDictionary<TKey, TValue>, ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation>,
        IRemovedNodePruning<ORMultiValueDictionary<TKey, TValue>>,
        IReplicatedDataSerialization, IEquatable<ORMultiValueDictionary<TKey, TValue>>,
        IEnumerable<KeyValuePair<TKey, IImmutableSet<TValue>>>, IORMultiValueDictionary
    {
        public static readonly ORMultiValueDictionary<TKey, TValue> Empty = new ORMultiValueDictionary<TKey, TValue>(ORDictionary<TKey, ORSet<TValue>>.Empty, withValueDeltas: false);
        public static readonly ORMultiValueDictionary<TKey, TValue> EmptyWithValueDeltas = new ORMultiValueDictionary<TKey, TValue>(ORDictionary<TKey, ORSet<TValue>>.Empty, withValueDeltas: true);

        internal readonly ORDictionary<TKey, ORSet<TValue>> Underlying;
        private readonly bool _withValueDeltas;

        internal ORMultiValueDictionary(ORDictionary<TKey, ORSet<TValue>> underlying, bool withValueDeltas)
        {
            Underlying = underlying;
            _withValueDeltas = withValueDeltas;
        }

        public bool DeltaValues => _withValueDeltas;

        public IImmutableDictionary<TKey, IImmutableSet<TValue>> Entries =>
            _withValueDeltas
                ? Underlying.Entries
                    .Where(kv => Underlying.KeySet.Contains(kv.Key))
                    .Select(kv => new KeyValuePair<TKey, IImmutableSet<TValue>>(kv.Key, kv.Value.Elements))
                    .ToImmutableDictionary()
                : Underlying.Entries
                    .Select(kv => new KeyValuePair<TKey, IImmutableSet<TValue>>(kv.Key, kv.Value.Elements))
                    .ToImmutableDictionary();

        public IImmutableSet<TValue> this[TKey key] => Underlying[key].Elements;

        public bool TryGetValue(TKey key, out IImmutableSet<TValue> value)
        {
            if (!_withValueDeltas || Underlying.KeySet.Contains(key))
            {
                ORSet<TValue> set;
                if (Underlying.TryGetValue(key, out set))
                {
                    value = set.Elements;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public bool ContainsKey(TKey key) => Underlying.KeySet.Contains(key);

        public bool IsEmpty => Underlying.IsEmpty;

        public int Count => Underlying.Count;

        /// <summary>
        /// Returns all keys stored within current ORMultiDictionary.
        /// </summary>
        public IEnumerable<TKey> Keys => Underlying.KeySet;

        /// <summary>
        /// Returns all values stored in all buckets within current ORMultiDictionary.
        /// </summary>
        public IEnumerable<TValue> Values
        {
            get
            {
                foreach (var value in Underlying.Values)
                foreach (var v in value)
                {
                    yield return v;
                }
            }
        }

        /// <summary>
        /// Sets a <paramref name="bucket"/> of values inside current dictionary under provided <paramref name="key"/>
        /// in the context of the provided cluster <paramref name="node"/>.
        /// </summary>
        public ORMultiValueDictionary<TKey, TValue> SetItems(Cluster.Cluster node, TKey key, IImmutableSet<TValue> bucket) =>
            SetItems(node.SelfUniqueAddress, key, bucket);

        /// <summary>
        /// Sets a <paramref name="bucket"/> of values inside current dictionary under provided <paramref name="key"/>
        /// in the context of the provided cluster <paramref name="node"/>.
        /// </summary>
        public ORMultiValueDictionary<TKey, TValue> SetItems(UniqueAddress node, TKey key, IImmutableSet<TValue> bucket)
        {
            var newUnderlying = Underlying.AddOrUpdate(node, key, ORSet<TValue>.Empty, _withValueDeltas, old =>
                bucket.Aggregate(old.Clear(node), (set, element) => set.Add(node, element)));

            return new ORMultiValueDictionary<TKey, TValue>(newUnderlying, _withValueDeltas);
        }

        /// <summary>
        /// Removes all values inside current dictionary stored under provided <paramref name="key"/>
        /// in the context of the provided cluster <paramref name="node"/>.
        /// </summary>
        public ORMultiValueDictionary<TKey, TValue> Remove(Cluster.Cluster node, TKey key) =>
            Remove(node.SelfUniqueAddress, key);

        /// <summary>
        /// Removes all values inside current dictionary stored under provided <paramref name="key"/>
        /// in the context of the provided cluster <paramref name="node"/>.
        /// </summary>
        public ORMultiValueDictionary<TKey, TValue> Remove(UniqueAddress node, TKey key)
        {
            if (_withValueDeltas)
            {
                var u = Underlying.AddOrUpdate(node, key, ORSet<TValue>.Empty, true, existing => existing.Clear(node));
                return new ORMultiValueDictionary<TKey, TValue>(u.RemoveKey(node, key), _withValueDeltas);
            }
            else
                return new ORMultiValueDictionary<TKey, TValue>(Underlying.Remove(node, key), _withValueDeltas);
        }

        public ORMultiValueDictionary<TKey, TValue> Merge(ORMultiValueDictionary<TKey, TValue> other)
        {
            if (_withValueDeltas == other._withValueDeltas)
            {
                return _withValueDeltas
                    ? new ORMultiValueDictionary<TKey, TValue>(Underlying.MergeRetainingDeletedValues(other.Underlying), _withValueDeltas)
                    : new ORMultiValueDictionary<TKey, TValue>(Underlying.Merge(other.Underlying), _withValueDeltas);
            }

            throw new ArgumentException($"Trying to merge two ORMultiValueDictionaries of different map sub-types");
        }

        /// <summary>
        /// Add an element to a set associated with a key. If there is no existing set then one will be initialised.
        /// </summary>
        public ORMultiValueDictionary<TKey, TValue> AddItem(Cluster.Cluster node, TKey key, TValue element) =>
            AddItem(node.SelfUniqueAddress, key, element);

        /// <summary>
        /// Add an element to a set associated with a key. If there is no existing set then one will be initialised.
        /// </summary>
        public ORMultiValueDictionary<TKey, TValue> AddItem(UniqueAddress node, TKey key, TValue element)
        {
            var newUnderlying = Underlying.AddOrUpdate(node, key, ORSet<TValue>.Empty, _withValueDeltas, x => x.Add(node, element));
            return new ORMultiValueDictionary<TKey, TValue>(newUnderlying, _withValueDeltas);
        }

        /// <summary>
        /// Remove an element of a set associated with a key. If there are no more elements in the set then the
        /// entire set will be removed.
        /// </summary>
        public ORMultiValueDictionary<TKey, TValue> RemoveItem(Cluster.Cluster node, TKey key, TValue element) =>
            RemoveItem(node.SelfUniqueAddress, key, element);

        /// <summary>
        /// Remove an element of a set associated with a key. If there are no more elements in the set then the
        /// entire set will be removed.
        /// </summary>
        public ORMultiValueDictionary<TKey, TValue> RemoveItem(UniqueAddress node, TKey key, TValue element)
        {
            var newUnderlying = Underlying.AddOrUpdate(node, key, ORSet<TValue>.Empty, _withValueDeltas, set => set.Remove(node, element));
            ORSet<TValue> found;
            if (newUnderlying.TryGetValue(key, out found) && found.IsEmpty)
            {
                if (_withValueDeltas)
                    newUnderlying = newUnderlying.RemoveKey(node, key);
                else
                    newUnderlying = newUnderlying.Remove(node, key);
            }

            return new ORMultiValueDictionary<TKey, TValue>(newUnderlying, _withValueDeltas);
        }

        /// <summary>
        /// Replace an element of a set associated with a key with a new one if it is different. This is useful when an element is removed
        /// and another one is added within the same Update. The order of addition and removal is important in order
        /// to retain history for replicated data.
        /// </summary>
        public ORMultiValueDictionary<TKey, TValue> ReplaceItem(Cluster.Cluster node, TKey key, TValue oldElement,
            TValue newElement) =>
            ReplaceItem(node.SelfUniqueAddress, key, oldElement, newElement);

        /// <summary>
        /// Replace an element of a set associated with a key with a new one if it is different. This is useful when an element is removed
        /// and another one is added within the same Update. The order of addition and removal is important in order
        /// to retain history for replicated data.
        /// </summary>
        public ORMultiValueDictionary<TKey, TValue> ReplaceItem(UniqueAddress node, TKey key, TValue oldElement, TValue newElement) =>
            !Equals(newElement, oldElement)
                ? AddItem(node, key, newElement).RemoveItem(node, key, oldElement)
                : this;

        public IReplicatedData Merge(IReplicatedData other) =>
            Merge((ORMultiValueDictionary<TKey, TValue>)other);

        public ImmutableHashSet<UniqueAddress> ModifiedByNodes => Underlying.ModifiedByNodes;
        public bool NeedPruningFrom(UniqueAddress removedNode) => Underlying.NeedPruningFrom(removedNode);
        IReplicatedData IRemovedNodePruning.PruningCleanup(UniqueAddress removedNode) => PruningCleanup(removedNode);

        IReplicatedData IRemovedNodePruning.Prune(UniqueAddress removedNode, UniqueAddress collapseInto) => Prune(removedNode, collapseInto);

        public ORMultiValueDictionary<TKey, TValue> Prune(UniqueAddress removedNode, UniqueAddress collapseInto) =>
            new ORMultiValueDictionary<TKey, TValue>(Underlying.Prune(removedNode, collapseInto), _withValueDeltas);

        public ORMultiValueDictionary<TKey, TValue> PruningCleanup(UniqueAddress removedNode) =>
            new ORMultiValueDictionary<TKey, TValue>(Underlying.PruningCleanup(removedNode), _withValueDeltas);

        public bool Equals(ORMultiValueDictionary<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Underlying, other.Underlying);
        }

        public IEnumerator<KeyValuePair<TKey, IImmutableSet<TValue>>> GetEnumerator() =>
            Underlying.Select(x => new KeyValuePair<TKey, IImmutableSet<TValue>>(x.Key, x.Value.Elements)).GetEnumerator();

        public override bool Equals(object obj) =>
            obj is ORMultiValueDictionary<TKey, TValue> && Equals((ORMultiValueDictionary<TKey, TValue>)obj);

        public override int GetHashCode() => Underlying.GetHashCode();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            var sb = new StringBuilder("ORMutliDictionary(");
            foreach (var entry in Entries)
            {
                sb.Append(entry.Key).Append("-> [");
                foreach (var value in entry.Value)
                {
                    sb.Append(value).Append(", ");
                }
                sb.Append("], ");
            }
            sb.Append(')');
            return sb.ToString();
        }

        #region delta

        internal sealed class ORMultiValueDictionaryDelta : ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation, IReplicatedDeltaSize, IORMultiValueDictionaryDeltaOperation
        {
            internal readonly ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation Underlying;

            public bool WithValueDeltas { get; }

            public ORMultiValueDictionaryDelta(ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation underlying, bool withValueDeltas)
            {
                Underlying = underlying;
                WithValueDeltas = withValueDeltas;
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
                if (other is ORMultiValueDictionaryDelta d)
                {
                    return new ORMultiValueDictionaryDelta((ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation)Underlying.Merge(d.Underlying), WithValueDeltas || d.WithValueDeltas);
                }

                return new ORMultiValueDictionaryDelta((ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation)Underlying.Merge(other), WithValueDeltas);
            }

            public IDeltaReplicatedData Zero => WithValueDeltas ? ORMultiValueDictionary<TKey, TValue>.EmptyWithValueDeltas : ORMultiValueDictionary<TKey, TValue>.Empty;

            public override bool Equals(object obj)
            {
                return obj is ORMultiValueDictionary<TKey, TValue>.ORMultiValueDictionaryDelta operation && 
                    Equals(operation.Underlying);
            }

            public bool Equals(ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation other)
            {
                if (other is ORDictionary<TKey, ORSet<TValue>>.DeltaGroup group)
                {
                    if (Underlying is ORDictionary<TKey, ORSet<TValue>>.DeltaGroup ourGroup)
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
            ORDictionary.IDeltaOperation IORMultiValueDictionaryDeltaOperation.Underlying => (ORDictionary.IDeltaOperation)Underlying;
        }

        // TODO: optimize this so it doesn't allocate each time it's called
        public ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation Delta => new ORMultiValueDictionaryDelta(Underlying.Delta, _withValueDeltas);

        public ORMultiValueDictionary<TKey, TValue> MergeDelta(ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation delta)
        {
            if (delta is ORMultiValueDictionaryDelta ormmd)
                delta = ormmd.Underlying;

            if (_withValueDeltas)
                return new ORMultiValueDictionary<TKey, TValue>(Underlying.MergeDeltaRetainingDeletedValues(delta), _withValueDeltas);
            else
                return new ORMultiValueDictionary<TKey, TValue>(Underlying.MergeDelta(delta), _withValueDeltas);
        }

        IReplicatedDelta IDeltaReplicatedData.Delta => Delta;

        IReplicatedData IDeltaReplicatedData.MergeDelta(IReplicatedDelta delta) 
        {
            switch (delta)
            {
                case ORMultiValueDictionaryDelta d:
                    return MergeDelta(d.Underlying);
                default:
                    return MergeDelta((ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation)delta);
            }
        }

        IReplicatedData IDeltaReplicatedData.ResetDelta() => ResetDelta();

        public ORMultiValueDictionary<TKey, TValue> ResetDelta() =>
            new ORMultiValueDictionary<TKey, TValue>(Underlying.ResetDelta(), _withValueDeltas);

        #endregion

        public Type KeyType { get; } = typeof(TKey);
        public Type ValueType { get; } = typeof(TValue);
    }
}
