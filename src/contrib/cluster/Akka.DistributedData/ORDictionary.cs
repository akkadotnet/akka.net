//-----------------------------------------------------------------------
// <copyright file="ORDictionary.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster;
using Akka.Pattern;

namespace Akka.DistributedData
{
    [Serializable]
    public sealed class ORDictionaryKey<TKey, TValue> : Key<ORDictionary<TKey, TValue>> where TValue : IReplicatedData
    {
        public ORDictionaryKey(string id) : base(id) { }
    }

    public interface IORDictionary
    {
        
    }

    public interface IORDictionary<TKey, TValue> : IORDictionary
    {
        
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
    public class ORDictionary<TKey, TValue> : IReplicatedData<ORDictionary<TKey, TValue>>, IORDictionary<TKey, TValue>,
        IRemovedNodePruning<ORDictionary<TKey, TValue>>, IEquatable<ORDictionary<TKey, TValue>>, IReplicatedDataSerialization
        where TValue : IReplicatedData
    {
        public static readonly ORDictionary<TKey, TValue> Empty = new ORDictionary<TKey, TValue>(ORSet<TKey>.Empty, ImmutableDictionary<TKey, TValue>.Empty);

        internal readonly ORSet<TKey> Keys;
        internal readonly IImmutableDictionary<TKey, TValue> Values;

        public ORDictionary(ORSet<TKey> keys, IImmutableDictionary<TKey, TValue> values)
        {
            Keys = keys;
            Values = values;
        }

        public IImmutableDictionary<TKey, TValue> Entries => Values;

        public TValue this[TKey key] => Values[key];

        public bool TryGetValue(TKey key, out TValue value) => Values.TryGetValue(key, out value);

        public bool ContainsKey(TKey key) => Values.ContainsKey(key);

        public bool IsEmpty => Values.Count == 0;

        public int Count => Values.Count;

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
            if (value is IORSet && Values.ContainsKey(key))
                throw new ArgumentException("ORDictionary.SetItem may not be used to replace an existing ORSet", nameof(value));

            return new ORDictionary<TKey, TValue>(Keys.Add(node, key), Values.SetItem(key, value));
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
            return Values.TryGetValue(key, out value)
                ? new ORDictionary<TKey, TValue>(Keys.Add(node, key), Values.SetItem(key, modify(value)))
                : new ORDictionary<TKey, TValue>(Keys.Add(node, key), Values.SetItem(key, modify(initial)));
        }

        /// <summary>
        /// Removes an entry from the map.
        /// Note that if there is a conflicting update on another node the entry will
        /// not be removed after merge.
        /// </summary>
        public ORDictionary<TKey, TValue> Remove(UniqueAddress node, TKey key) =>
            new ORDictionary<TKey, TValue>(Keys.Remove(node, key), Values.Remove(key));

        public ORDictionary<TKey, TValue> Merge(ORDictionary<TKey, TValue> other)
        {
            var mergedKeys = Keys.Merge(other.Keys);
            var mergedValues = ImmutableDictionary<TKey, TValue>.Empty.ToBuilder();
            foreach (var key in mergedKeys.Elements)
            {
                TValue left, right;
                var leftFound = Values.TryGetValue(key, out left);
                var rightFound = other.Values.TryGetValue(key, out right);
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
            return Keys.NeedPruningFrom(removedNode) || Values.Any(x =>
            {
                var data = x.Value as IRemovedNodePruning;
                return data != null && data.NeedPruningFrom(removedNode);
            });
        }

        public ORDictionary<TKey, TValue> Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
        {
            var prunedKeys = Keys.Prune(removedNode, collapseInto);
            var prunedValues = Values.Aggregate(Values, (acc, kv) =>
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
            var pruningCleanupKeys = Keys.PruningCleanup(removedNode);
            var pruningCleanupValues = Values.Aggregate(Values, (acc, kv) =>
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

            return Equals(Keys, other.Keys) && Values.SequenceEqual(other.Values);
        }

        public override bool Equals(object obj)
        {
            return obj is ORDictionary<TKey, TValue> && Equals((ORDictionary<TKey, TValue>) obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Keys.GetHashCode() *397) ^ Values.GetHashCode());
            }
        }
    }
}