//-----------------------------------------------------------------------
// <copyright file="ORDictionary.cs" company="Akka.NET Project">
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
using Akka.Pattern;
using Akka.Util;

namespace Akka.DistributedData
{
    [Serializable]
    public sealed class ORDictionaryKey<TKey, TValue> : Key<ORDictionary<TKey, TValue>> where TValue : IReplicatedData
    {
        public ORDictionaryKey(string id) : base(id) { }
    }
    
    public static class ORDictionary
    {
        public static ORDictionary<TKey, TValue> Create<TKey, TValue>(UniqueAddress node, TKey key, TValue value) where TValue : IReplicatedData =>
            ORDictionary<TKey, TValue>.Empty.SetItem(node, key, value);

        public static ORDictionary<TKey, TValue> Create<TKey, TValue>(params Tuple<UniqueAddress, TKey, TValue>[] elements) where TValue : IReplicatedData =>
            elements.Aggregate(ORDictionary<TKey, TValue>.Empty, (acc, t) => acc.SetItem(t.Item1, t.Item2, t.Item3));

        public static ORDictionary<TKey, TValue> Create<TKey, TValue>(IEnumerable<Tuple<UniqueAddress, TKey, TValue>> elements) where TValue : IReplicatedData =>
            elements.Aggregate(ORDictionary<TKey, TValue>.Empty, (acc, t) => acc.SetItem(t.Item1, t.Item2, t.Item3));
    }

    /// <summary>
    /// Implements a 'Observed Remove Map' CRDT, also called a 'OR-Map'.
    /// 
    /// It has similar semantics as an <see cref="ORSet{T}"/>, but in case of concurrent updates
    /// the values are merged, and must therefore be <see cref="IReplicatedData"/> types themselves.
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    [Serializable]
    public class ORDictionary<TKey, TValue> : IReplicatedData<ORDictionary<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>,
        IRemovedNodePruning<ORDictionary<TKey, TValue>>, IEquatable<ORDictionary<TKey, TValue>>, IReplicatedDataSerialization
        where TValue : IReplicatedData
    {
        /// <summary>
        /// An empty instance of the <see cref="ORDictionary{TKey,TValue}"/>
        /// </summary>
        public static readonly ORDictionary<TKey, TValue> Empty = new ORDictionary<TKey, TValue>(ORSet<TKey>.Empty, ImmutableDictionary<TKey, TValue>.Empty);

        internal readonly ORSet<TKey> KeySet;
        internal readonly IImmutableDictionary<TKey, TValue> ValueMap;

        /// <summary>
        /// Creates a new instance of the <see cref="ORDictionary{TKey,TValue}"/> class.
        /// </summary>
        /// <param name="keySet"></param>
        /// <param name="valueMap"></param>
        public ORDictionary(ORSet<TKey> keySet, IImmutableDictionary<TKey, TValue> valueMap)
        {
            KeySet = keySet;
            ValueMap = valueMap;
        }

        /// <summary>
        /// Returns all keys stored within current <see cref="ORDictionary{TKey,TValue}"/>
        /// </summary>
        public IEnumerable<TKey> Keys => KeySet;

        /// <summary>
        /// Returns all values stored within current <see cref="ORDictionary{TKey,TValue}"/>
        /// </summary>
        public IEnumerable<TValue> Values => ValueMap.Values;

        /// <summary>
        /// Returns all entries stored within current <see cref="ORDictionary{TKey,TValue}"/>
        /// </summary>
        public IImmutableDictionary<TKey, TValue> Entries => ValueMap;

        /// <summary>
        /// Returns an element stored under provided <paramref name="key"/>.
        /// </summary>
        public TValue this[TKey key] => ValueMap[key];

        /// <summary>
        /// Tries to retrieve value under provided <paramref name="key"/>, 
        /// returning true if value under that key has been found.
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value) => ValueMap.TryGetValue(key, out value);

        /// <summary>
        /// Checks if provided <paramref name="key"/> can be found inside current <see cref="ORDictionary{TKey,TValue}"/>
        /// </summary>
        public bool ContainsKey(TKey key) => ValueMap.ContainsKey(key);

        /// <summary>
        /// Determines if current <see cref="ORDictionary{TKey,TValue}"/> doesn't contain any value.
        /// </summary>
        public bool IsEmpty => ValueMap.Count == 0;

        /// <summary>
        /// Returns number of entries stored within current <see cref="ORDictionary{TKey,TValue}"/>
        /// </summary>
        public int Count => ValueMap.Count;

        /// <summary>
        /// Adds an entry to the map.
        /// Note that the new `value` will be merged with existing values
        /// on other nodes and the outcome depends on what `ReplicatedData`
        /// type that is used.
        /// 
        /// Consider using [[#updated]] instead of `put` if you want modify
        /// existing entry.
        /// 
        /// `IllegalArgumentException` is thrown if you try to replace an existing `ORSet`
        /// value, because important history can be lost when replacing the `ORSet` and
        /// undesired effects of merging will occur. Use [[ORMultiMap]] or [[#updated]] instead.
        /// </summary>
        public ORDictionary<TKey, TValue> SetItem(UniqueAddress node, TKey key, TValue value)
        {
            if (value is IORSet && ValueMap.ContainsKey(key))
                throw new ArgumentException("ORDictionary.SetItems may not be used to replace an existing ORSet", nameof(value));

            return new ORDictionary<TKey, TValue>(KeySet.Add(node, key), ValueMap.SetItem(key, value));
        }

        /// <summary>
        /// Replace a value by applying the <paramref name="modify"/> function on the existing value.
        /// 
        /// If there is no current value for the <paramref name="key"/> the <paramref name="initial"/> value will be
        /// passed to the <paramref name="modify"/> function.
        /// </summary>
        public ORDictionary<TKey, TValue> AddOrUpdate(UniqueAddress node, TKey key, TValue initial,
            Func<TValue, TValue> modify)
        {
            TValue value;
            return ValueMap.TryGetValue(key, out value)
                ? new ORDictionary<TKey, TValue>(KeySet.Add(node, key), ValueMap.SetItem(key, modify(value)))
                : new ORDictionary<TKey, TValue>(KeySet.Add(node, key), ValueMap.SetItem(key, modify(initial)));
        }

        /// <summary>
        /// Removes an entry from the map.
        /// Note that if there is a conflicting update on another node the entry will
        /// not be removed after merge.
        /// </summary>
        public ORDictionary<TKey, TValue> Remove(UniqueAddress node, TKey key) =>
            new ORDictionary<TKey, TValue>(KeySet.Remove(node, key), ValueMap.Remove(key));

        public ORDictionary<TKey, TValue> Merge(ORDictionary<TKey, TValue> other)
        {
            var mergedKeys = KeySet.Merge(other.KeySet);
            var mergedValues = ImmutableDictionary<TKey, TValue>.Empty.ToBuilder();
            foreach (var key in mergedKeys.Elements)
            {
                TValue left, right;
                var leftFound = ValueMap.TryGetValue(key, out left);
                var rightFound = other.ValueMap.TryGetValue(key, out right);
                if (leftFound && rightFound)
                    mergedValues.Add(key, (TValue) left.Merge(right));
                else if (leftFound)
                    mergedValues.Add(key, left);
                else if (rightFound)
                    mergedValues.Add(key, right);
                else
                    throw new IllegalStateException($"Missing value for '{key}'");
            }

            return new ORDictionary<TKey, TValue>(mergedKeys, mergedValues.ToImmutable());
        }

        public IReplicatedData Merge(IReplicatedData other) => Merge((ORDictionary<TKey, TValue>)other);

        public bool NeedPruningFrom(UniqueAddress removedNode)
        {
            return KeySet.NeedPruningFrom(removedNode) || ValueMap.Any(x =>
            {
                var data = x.Value as IRemovedNodePruning;
                return data != null && data.NeedPruningFrom(removedNode);
            });
        }

        public ORDictionary<TKey, TValue> Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
        {
            var prunedKeys = KeySet.Prune(removedNode, collapseInto);
            var prunedValues = ValueMap.Aggregate(ValueMap, (acc, kv) =>
            {
                var data = kv.Value as IRemovedNodePruning;
                return data != null && data.NeedPruningFrom(removedNode)
                    ? acc.SetItem(kv.Key, (TValue) data.Prune(removedNode, collapseInto))
                    : acc;
            });

            return new ORDictionary<TKey, TValue>(prunedKeys, prunedValues);
        }

        public ORDictionary<TKey, TValue> PruningCleanup(UniqueAddress removedNode)
        {
            var pruningCleanupKeys = KeySet.PruningCleanup(removedNode);
            var pruningCleanupValues = ValueMap.Aggregate(ValueMap, (acc, kv) =>
            {
                var data = kv.Value as IRemovedNodePruning;
                return data != null && data.NeedPruningFrom(removedNode)
                    ? acc.SetItem(kv.Key, (TValue)data.PruningCleanup(removedNode))
                    : acc;
            });

            return new ORDictionary<TKey, TValue>(pruningCleanupKeys, pruningCleanupValues);
        }

        public bool Equals(ORDictionary<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(KeySet, other.KeySet) && ValueMap.SequenceEqual(other.ValueMap);
        }

        public override bool Equals(object obj)
        {
            return obj is ORDictionary<TKey, TValue> && Equals((ORDictionary<TKey, TValue>) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((KeySet.GetHashCode() *397) ^ ValueMap.GetHashCode());
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => ValueMap.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            var sb = new StringBuilder("ORDictionary(");
            foreach (var entry in Entries)
            {
                sb.Append(entry.Key).Append("->").Append(entry.Value).Append(", ");
            }
            sb.Append(')');
            return sb.ToString();
        }
    }
}