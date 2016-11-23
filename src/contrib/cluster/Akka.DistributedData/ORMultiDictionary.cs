//-----------------------------------------------------------------------
// <copyright file="ORMultiDictionary.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData
{
    [Serializable]
    public sealed class ORMultiDictionaryKey<TKey, TValue> : Key<ORMultiDictionary<TKey, TValue>>
    {
        public ORMultiDictionaryKey(string id) : base(id)
        {
        }
    }

    /// <summary>
    /// An immutable multi-map implementation. This class wraps an
    /// <see cref="ORDictionary{TKey,TValue}"/> with an <see cref="ORSet{T}"/> for the map's value.
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    [Serializable]
    public class ORMultiDictionary<TKey, TValue> :
        IReplicatedData<ORMultiDictionary<TKey, TValue>>,
        IRemovedNodePruning<ORMultiDictionary<TKey, TValue>>,
        IReplicatedDataSerialization, IEquatable<ORMultiDictionary<TKey, TValue>>,
        IEnumerable<KeyValuePair<TKey, IImmutableSet<TValue>>>
    {
        public static readonly ORMultiDictionary<TKey, TValue> Empty = new ORMultiDictionary<TKey, TValue>(ORDictionary<TKey, ORSet<TValue>>.Empty);

        private readonly ORDictionary<TKey, ORSet<TValue>> _underlying;

        public ORMultiDictionary(ORDictionary<TKey, ORSet<TValue>> underlying)
        {
            _underlying = underlying;
        }

        public IImmutableDictionary<TKey, IImmutableSet<TValue>> Entries => _underlying.Entries
                .Select(kv => new KeyValuePair<TKey, IImmutableSet<TValue>>(kv.Key, kv.Value.Elements))
                .ToImmutableDictionary();

        public IImmutableSet<TValue> this[TKey key] => _underlying[key].Elements;

        public bool TryGetValue(TKey key, out IImmutableSet<TValue> value)
        {
            ORSet<TValue> set;
            if (_underlying.TryGetValue(key, out set))
            {
                value = set.Elements;
                return true;
            }

            value = null;
            return false;
        }

        public bool ContainsKey(TKey key) => _underlying.ContainsKey(key);

        public bool IsEmpty => _underlying.IsEmpty;

        public int Count => _underlying.Count;

        /// <summary>
        /// Returns all keys stored within current ORMultiDictionary.
        /// </summary>
        public IEnumerable<TKey> Keys => _underlying.KeySet;

        /// <summary>
        /// Returns all values stored in all buckets within current ORMultiDictionary.
        /// </summary>
        public IEnumerable<TValue> Values
        {
            get
            {
                foreach (var value in _underlying.Values)
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
        public ORMultiDictionary<TKey, TValue> SetItems(UniqueAddress node, TKey key, IImmutableSet<TValue> bucket)
        {
            var newUnderlying = _underlying.AddOrUpdate(node, key, ORSet<TValue>.Empty, old => 
                bucket.Aggregate(old.Clear(node), (set, element) => set.Add(node, element)));

            return new ORMultiDictionary<TKey, TValue>(newUnderlying);
        }

        /// <summary>
        /// Removes all values inside current dictionary stored under provided <paramref name="key"/>
        /// in the context of the provided cluster <paramref name="node"/>.
        /// </summary>
        public ORMultiDictionary<TKey, TValue> Remove(UniqueAddress node, TKey key) => 
            new ORMultiDictionary<TKey, TValue>(_underlying.Remove(node, key));

        public ORMultiDictionary<TKey, TValue> Merge(ORMultiDictionary<TKey, TValue> other) =>
            new ORMultiDictionary<TKey, TValue>(_underlying.Merge(other._underlying));

        /// <summary>
        /// Add an element to a set associated with a key. If there is no existing set then one will be initialised.
        /// </summary>
        public ORMultiDictionary<TKey, TValue> AddItem(UniqueAddress node, TKey key, TValue element) => 
            new ORMultiDictionary<TKey, TValue>(_underlying.AddOrUpdate(node, key, ORSet<TValue>.Empty, set => set.Add(node, element)));

        /// <summary>
        /// Remove an element of a set associated with a key. If there are no more elements in the set then the
        /// entire set will be removed.
        /// </summary>
        public ORMultiDictionary<TKey, TValue> RemoveItem(UniqueAddress node, TKey key, TValue element)
        {
            var newUnderlying = _underlying.AddOrUpdate(node, key, ORSet<TValue>.Empty, set => set.Remove(node, element));
            ORSet<TValue> found;
            if (newUnderlying.TryGetValue(key, out found) && found.IsEmpty)
            {
                newUnderlying = newUnderlying.Remove(node, key);
            }

            return new ORMultiDictionary<TKey, TValue>(newUnderlying);
        }

        /// <summary>
        /// Replace an element of a set associated with a key with a new one if it is different. This is useful when an element is removed
        /// and another one is added within the same Update. The order of addition and removal is important in order
        /// to retain history for replicated data.
        /// </summary>
        public ORMultiDictionary<TKey, TValue> ReplaceItem(UniqueAddress node, TKey key, TValue oldElement, TValue newElement) => 
            !Equals(newElement, oldElement) 
            ? AddItem(node, key, newElement).RemoveItem(node, key, oldElement) 
            : this;

        public IReplicatedData Merge(IReplicatedData other) =>
            Merge((ORMultiDictionary<TKey, TValue>)other);

        public bool NeedPruningFrom(UniqueAddress removedNode) => _underlying.NeedPruningFrom(removedNode);

        public ORMultiDictionary<TKey, TValue> Prune(UniqueAddress removedNode, UniqueAddress collapseInto) => 
            new ORMultiDictionary<TKey, TValue>(_underlying.Prune(removedNode, collapseInto));

        public ORMultiDictionary<TKey, TValue> PruningCleanup(UniqueAddress removedNode) => 
            new ORMultiDictionary<TKey, TValue>(_underlying.PruningCleanup(removedNode));

        public bool Equals(ORMultiDictionary<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(_underlying, other._underlying);
        }

        public IEnumerator<KeyValuePair<TKey, IImmutableSet<TValue>>> GetEnumerator() => 
            _underlying.Select(x => new KeyValuePair<TKey, IImmutableSet<TValue>>(x.Key, x.Value.Elements)).GetEnumerator();

        public override bool Equals(object obj) => 
            obj is ORMultiDictionary<TKey, TValue> && Equals((ORMultiDictionary<TKey, TValue>) obj);

        public override int GetHashCode() => _underlying.GetHashCode();
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
    }
}