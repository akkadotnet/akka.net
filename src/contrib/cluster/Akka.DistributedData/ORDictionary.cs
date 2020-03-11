//-----------------------------------------------------------------------
// <copyright file="ORDictionary.cs" company="Akka.NET Project">
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
using Akka.Pattern;

namespace Akka.DistributedData
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Marker interface for serialization
    /// </summary>
    internal interface IORDictionaryKey
    {
        Type KeyType { get; }

        Type ValueType { get; }
    }

    [Serializable]
    public sealed class ORDictionaryKey<TKey, TValue> : Key<ORDictionary<TKey, TValue>>, IORDictionaryKey where TValue : IReplicatedData<TValue>
    {
        public ORDictionaryKey(string id) : base(id) { }
        public Type KeyType { get; } = typeof(TKey);
        public Type ValueType { get; } = typeof(TValue);
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Marker interface for serialization
    /// </summary>
    internal interface IORDictionary
    {
        Type KeyType { get; }

        Type ValueType { get; }
    }

    public static class ORDictionary
    {
        /// <summary>
        /// INTERNAL API
        ///
        /// Used for serialization purposes.
        /// </summary>
        internal interface IDeltaOperation
        {
            Type KeyType { get; }

            Type ValueType { get; }
        }

        /// <summary>
        /// INTERNAL API
        ///
        /// Used for serialization purposes.
        /// </summary>
        internal interface IPutDeltaOp : IDeltaOperation { }

        /// <summary>
        /// INTERNAL API
        ///
        /// Used for serialization purposes.
        /// </summary>
        internal interface IRemoveDeltaOp : IDeltaOperation { }

        /// <summary>
        /// INTERNAL API
        ///
        /// Used for serialization purposes.
        /// </summary>
        internal interface IRemoveKeyDeltaOp : IDeltaOperation { }

        /// <summary>
        /// INTERNAL API
        ///
        /// Used for serialization purposes.
        /// </summary>
        internal interface IUpdateDeltaOp : IDeltaOperation { }

        /// <summary>
        /// INTERNAL API
        ///
        /// Used for serialization purposes.
        /// </summary>
        internal interface IDeltaGroupOp : IDeltaOperation
        {
            IReadOnlyList<IDeltaOperation> OperationsSerialization { get; }
        }

        public static ORDictionary<TKey, TValue> Create<TKey, TValue>(UniqueAddress node, TKey key, TValue value) where TValue : IReplicatedData<TValue> =>
            ORDictionary<TKey, TValue>.Empty.SetItem(node, key, value);

        public static ORDictionary<TKey, TValue> Create<TKey, TValue>(params (UniqueAddress, TKey, TValue)[] elements) where TValue : IReplicatedData<TValue> =>
            elements.Aggregate(ORDictionary<TKey, TValue>.Empty, (acc, t) => acc.SetItem(t.Item1, t.Item2, t.Item3));

        public static ORDictionary<TKey, TValue> Create<TKey, TValue>(IEnumerable<(UniqueAddress, TKey, TValue)> elements) where TValue : IReplicatedData<TValue> =>
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
    public sealed class ORDictionary<TKey, TValue> :
        IEnumerable<KeyValuePair<TKey, TValue>>,
        IRemovedNodePruning<ORDictionary<TKey, TValue>>,
        IEquatable<ORDictionary<TKey, TValue>>,
        IReplicatedDataSerialization,
        IDeltaReplicatedData<ORDictionary<TKey, TValue>, ORDictionary<TKey, TValue>.IDeltaOperation>, IORDictionary
        where TValue : IReplicatedData<TValue>
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
            : this(keySet, valueMap, null)
        {
        }

        internal ORDictionary(ORSet<TKey> keySet, IImmutableDictionary<TKey, TValue> valueMap, IDeltaOperation delta)
        {
            KeySet = keySet;
            ValueMap = valueMap;
            _syncRoot = delta;
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
        /// Note that the new <paramref name="value"/> will be merged with existing values
        /// on other nodes and the outcome depends on what <see cref="IReplicatedData"/>
        /// type that is used.
        /// 
        /// Consider using <see cref="AddOrUpdate(Akka.Cluster.Cluster, TKey, TValue, Func{TValue, TValue})">AddOrUpdate</see> instead of <see cref="SetItem(Akka.Cluster.Cluster,TKey,TValue)"/> if you want modify
        /// existing entry.
        /// 
        /// <see cref="ArgumentException"/> is thrown if you try to replace an existing <see cref="ORSet{T}"/>
        /// value, because important history can be lost when replacing the `ORSet` and
        /// undesired effects of merging will occur. Use <see cref="ORMultiValueDictionary{TKey,TValue}"/> or <see cref="AddOrUpdate(Akka.Cluster.Cluster, TKey, TValue, Func{TValue, TValue})">AddOrUpdate</see> instead.
        /// </summary>
        public ORDictionary<TKey, TValue> SetItem(Cluster.Cluster node, TKey key, TValue value) =>
            SetItem(node.SelfUniqueAddress, key, value);

        /// <summary>
        /// Adds an entry to the map.
        /// Note that the new <paramref name="value"/> will be merged with existing values
        /// on other nodes and the outcome depends on what <see cref="IReplicatedData"/>
        /// type that is used.
        /// 
        /// Consider using <see cref="AddOrUpdate(Akka.Cluster.Cluster, TKey, TValue, Func{TValue, TValue})">AddOrUpdate</see> instead of <see cref="SetItem(UniqueAddress,TKey,TValue)"/> if you want modify
        /// existing entry.
        /// 
        /// <see cref="ArgumentException"/> is thrown if you try to replace an existing <see cref="ORSet{T}"/>
        /// value, because important history can be lost when replacing the `ORSet` and
        /// undesired effects of merging will occur. Use <see cref="ORMultiValueDictionary{TKey,TValue}"/> or <see cref="AddOrUpdate(Akka.Cluster.Cluster, TKey, TValue, Func{TValue, TValue})">AddOrUpdate</see> instead.
        /// </summary>
        public ORDictionary<TKey, TValue> SetItem(UniqueAddress node, TKey key, TValue value)
        {
            if (value is IORSet && ValueMap.ContainsKey(key))
                throw new ArgumentException("ORDictionary.SetItems may not be used to replace an existing ORSet", nameof(value));

            var newKeys = KeySet.ResetDelta().Add(node, key);
            var delta = new PutDeltaOperation(newKeys.Delta, key, value);
            // put forcibly damages history, so we propagate full value that will overwrite previous values
            return new ORDictionary<TKey, TValue>(newKeys, ValueMap.SetItem(key, value), NewDelta(delta));
        }

        /// <summary>
        /// Replace a value by applying the <paramref name="modify"/> function on the existing value.
        /// 
        /// If there is no current value for the <paramref name="key"/> the <paramref name="initial"/> value will be
        /// passed to the <paramref name="modify"/> function.
        /// </summary>
        public ORDictionary<TKey, TValue> AddOrUpdate(Cluster.Cluster node, TKey key, TValue initial,
            Func<TValue, TValue> modify) => AddOrUpdate(node.SelfUniqueAddress, key, initial, modify);

        /// <summary>
        /// Replace a value by applying the <paramref name="modify"/> function on the existing value.
        /// 
        /// If there is no current value for the <paramref name="key"/> the <paramref name="initial"/> value will be
        /// passed to the <paramref name="modify"/> function.
        /// </summary>
        public ORDictionary<TKey, TValue> AddOrUpdate(UniqueAddress node, TKey key, TValue initial,
            Func<TValue, TValue> modify)
        {
            return AddOrUpdate(node, key, initial, false, modify);
        }

        internal ORDictionary<TKey, TValue> AddOrUpdate(UniqueAddress node, TKey key, TValue initial, bool valueDeltas,
            Func<TValue, TValue> modify)
        {
            bool hasOldValue;
            if (!ValueMap.TryGetValue(key, out var oldValue))
            {
                oldValue = initial;
                hasOldValue = false;
            }
            else hasOldValue = true;

            // Optimization: for some types - like GSet, GCounter, PNCounter and ORSet  - that are delta based
            // we can emit (and later merge) their deltas instead of full updates.
            // However to avoid necessity of tombstones, the derived map type needs to support this
            // with clearing the value (e.g. removing all elements if value is a set)
            // before removing the key - like e.g. ORMultiMap does
            var newKeys = KeySet.ResetDelta().Add(node, key);
            if (valueDeltas && oldValue is IDeltaReplicatedData deltaOldValue)
            {
                var newValue = modify((TValue)deltaOldValue.ResetDelta());
                var newValueDelta = ((IDeltaReplicatedData)newValue).Delta;
                if (newValueDelta != null && hasOldValue)
                {
                    var op = new UpdateDeltaOperation(newKeys.Delta, ImmutableDictionary.CreateRange(new[] { new KeyValuePair<TKey, IReplicatedData>(key, newValueDelta) }));
                    return new ORDictionary<TKey, TValue>(newKeys, ValueMap.SetItem(key, newValue), NewDelta(op));
                }
                else
                {
                    var op = new PutDeltaOperation(newKeys.Delta, key, newValue);
                    return new ORDictionary<TKey, TValue>(newKeys, ValueMap.SetItem(key, newValue), NewDelta(op));
                }
            }
            else
            {
                var newValue = modify(oldValue);
                var delta = new PutDeltaOperation(newKeys.Delta, key, newValue);
                return new ORDictionary<TKey, TValue>(newKeys, ValueMap.SetItem(key, newValue), NewDelta(delta));
            }
        }

        /// <summary>
        /// Removes an entry from the map.
        /// Note that if there is a conflicting update on another node the entry will
        /// not be removed after merge.
        /// </summary>
        public ORDictionary<TKey, TValue> Remove(Cluster.Cluster node, TKey key) =>
            Remove(node.SelfUniqueAddress, key);

        /// <summary>
        /// Removes an entry from the map.
        /// Note that if there is a conflicting update on another node the entry will
        /// not be removed after merge.
        /// </summary>
        public ORDictionary<TKey, TValue> Remove(UniqueAddress node, TKey key)
        {
            // for removals the delta values map emitted will be empty
            var newKeys = KeySet.ResetDelta().Remove(node, key);
            var removeOp = new RemoveDeltaOperation(newKeys.Delta);
            return new ORDictionary<TKey, TValue>(newKeys, ValueMap.Remove(key), NewDelta(removeOp));
        }

        public ORDictionary<TKey, TValue> Merge(ORDictionary<TKey, TValue> other)
        {
            var mergedKeys = KeySet.Merge(other.KeySet);
            return DryMerge(other, mergedKeys, mergedKeys.GetEnumerator());
        }

        private ORDictionary<TKey, TValue> DryMerge(ORDictionary<TKey, TValue> other, ORSet<TKey> mergedKeys, IEnumerator<TKey> valueKeysEnumerator)
        {
            var mergedValues = ImmutableDictionary<TKey, TValue>.Empty.ToBuilder();
            while (valueKeysEnumerator.MoveNext())
            {
                var key = valueKeysEnumerator.Current;
                TValue value2;
                if (ValueMap.TryGetValue(key, out var value1))
                {
                    if (other.ValueMap.TryGetValue(key, out value2))
                    {
                        var merged = value1.Merge(value2);
                        mergedValues[key] = merged;
                    }
                    else
                        mergedValues[key] = value1;
                }
                else
                {
                    if (other.ValueMap.TryGetValue(key, out value2))
                        mergedValues[key] = value2;
                    else
                        throw new IllegalStateException($"Missing value for {key}");
                }
            }

            return new ORDictionary<TKey, TValue>(mergedKeys, mergedValues.ToImmutable());
        }

        public IReplicatedData Merge(IReplicatedData other) => Merge((ORDictionary<TKey, TValue>)other);

        IReplicatedDelta IDeltaReplicatedData.Delta => Delta;

        IReplicatedData IDeltaReplicatedData.MergeDelta(IReplicatedDelta delta) => MergeDelta((IDeltaOperation)delta);
        IReplicatedData IDeltaReplicatedData.ResetDelta() => ResetDelta();

        public ImmutableHashSet<UniqueAddress> ModifiedByNodes =>
            KeySet.ModifiedByNodes.Union(ValueMap.Aggregate(ImmutableHashSet<UniqueAddress>.Empty, (acc, pair) =>
            {
                return pair.Value is IRemovedNodePruning pruning ? acc.Union(pruning.ModifiedByNodes) : acc;
            }));

        public bool NeedPruningFrom(UniqueAddress removedNode)
        {
            return KeySet.NeedPruningFrom(removedNode) || ValueMap.Any(x =>
            {
                return x.Value is IRemovedNodePruning data && data.NeedPruningFrom(removedNode);
            });
        }

        IReplicatedData IRemovedNodePruning.PruningCleanup(UniqueAddress removedNode) => PruningCleanup(removedNode);

        IReplicatedData IRemovedNodePruning.Prune(UniqueAddress removedNode, UniqueAddress collapseInto) => Prune(removedNode, collapseInto);

        public ORDictionary<TKey, TValue> Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
        {
            var prunedKeys = KeySet.Prune(removedNode, collapseInto);
            var prunedValues = ValueMap.Aggregate(ValueMap, (acc, kv) =>
            {
                return kv.Value is IRemovedNodePruning<TValue> data && data.NeedPruningFrom(removedNode)
                    ? acc.SetItem(kv.Key, data.Prune(removedNode, collapseInto))
                    : acc;
            });

            return new ORDictionary<TKey, TValue>(prunedKeys, prunedValues);
        }

        public ORDictionary<TKey, TValue> PruningCleanup(UniqueAddress removedNode)
        {
            var pruningCleanupKeys = KeySet.PruningCleanup(removedNode);
            var pruningCleanupValues = ValueMap.Aggregate(ValueMap, (acc, kv) =>
            {
                return kv.Value is IRemovedNodePruning<TValue> data && data.NeedPruningFrom(removedNode)
                    ? acc.SetItem(kv.Key, data.PruningCleanup(removedNode))
                    : acc;
            });

            return new ORDictionary<TKey, TValue>(pruningCleanupKeys, pruningCleanupValues);
        }

        /// <inheritdoc/>
        public bool Equals(ORDictionary<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(KeySet, other.KeySet) && ValueMap.SequenceEqual(other.ValueMap);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is ORDictionary<TKey, TValue> pairs && Equals(pairs);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((KeySet.GetHashCode() * 397) ^ ValueMap.GetHashCode());
            }
        }

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => ValueMap.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
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

        #region delta operations

        public interface IDeltaOperation : IReplicatedDelta, IRequireCausualDeliveryOfDeltas, IReplicatedDataSerialization, IEquatable<IDeltaOperation>
        {
        }

        internal abstract class AtomicDeltaOperation : IDeltaOperation, IReplicatedDeltaSize, ORDictionary.IDeltaOperation
        {
            public abstract ORSet<TKey>.IDeltaOperation Underlying { get; }
            public virtual IReplicatedData Merge(IReplicatedData other)
            {
                if (other is AtomicDeltaOperation)
                    return new DeltaGroup(ImmutableArray.Create(this, (IDeltaOperation)other));
                else
                {
                    var builder = ImmutableArray<IDeltaOperation>.Empty.ToBuilder();
                    builder.Add(this);
                    builder.AddRange(((DeltaGroup)other).Operations);
                    return new DeltaGroup(builder.ToImmutable());
                }
            }

            public IDeltaReplicatedData Zero => ORDictionary<TKey, TValue>.Empty;
            public int DeltaSize => 1;

            public abstract bool Equals(IDeltaOperation op);

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((IDeltaOperation)obj);
            }

            public Type KeyType { get; } = typeof(TKey);
            public Type ValueType { get; } = typeof(TValue);
        }

        internal sealed class PutDeltaOperation : AtomicDeltaOperation, ORDictionary.IPutDeltaOp
        {
            public override ORSet<TKey>.IDeltaOperation Underlying { get; }
            public TKey Key { get; }
            public TValue Value { get; }

            public PutDeltaOperation(ORSet<TKey>.IDeltaOperation underlying, TKey key, TValue value)
            {
                if (underlying == null) throw new ArgumentNullException(nameof(underlying));

                Underlying = underlying;
                Key = key;
                Value = value;
            }

            public override IReplicatedData Merge(IReplicatedData other)
            {
                UpdateDeltaOperation update;
                if (other is PutDeltaOperation put && Equals(Key, put.Key))
                {
                    return new PutDeltaOperation((ORSet<TKey>.IDeltaOperation)Underlying.Merge(put.Underlying), put.Key, put.Value);
                }
                else if ((update = other as UpdateDeltaOperation) != null && update.Values.Count == 1 && update.Values.ContainsKey(Key))
                {
                    var merged = (ORSet<TKey>.IDeltaOperation)this.Underlying.Merge(update.Underlying);
                    var e2 = update.Values.First().Value;
                    if (Value is IDeltaReplicatedData data)
                    {
                        var mergedDelta = data.MergeDelta((IReplicatedDelta)e2);
                        return new PutDeltaOperation(merged, Key, (TValue)mergedDelta);
                    }
                    else
                    {
                        var mergedDelta = Value.Merge(e2);
                        return new PutDeltaOperation(merged, Key, (TValue)mergedDelta);
                    }
                }
                else if (other is AtomicDeltaOperation)
                {
                    return new DeltaGroup(ImmutableArray.Create(this, (IDeltaOperation)other));
                }
                else
                {
                    var builder = ImmutableArray<IDeltaOperation>.Empty.ToBuilder();
                    builder.Add(this);
                    builder.AddRange(((DeltaGroup)other).Operations);
                    return new DeltaGroup(builder.ToImmutable());
                }
            }

            public override bool Equals(IDeltaOperation op)
            {
                if (ReferenceEquals(null, op)) return false;
                if (ReferenceEquals(this, op)) return true;
                if (op is PutDeltaOperation put)
                {
                    return Equals(Key, put.Key) && Equals(Value, put.Value) && Underlying.Equals(put.Underlying);
                }
                return false;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = Underlying.GetHashCode();
                    hash = (hash * 397) ^ Key.GetHashCode();
                    hash = (hash * 397) ^ Value.GetHashCode();
                    return hash;
                }
            }
        }

        internal sealed class UpdateDeltaOperation : AtomicDeltaOperation, ORDictionary.IUpdateDeltaOp
        {
            public override ORSet<TKey>.IDeltaOperation Underlying { get; }
            public ImmutableDictionary<TKey, IReplicatedData> Values { get; }

            public UpdateDeltaOperation(ORSet<TKey>.IDeltaOperation underlying, ImmutableDictionary<TKey, IReplicatedData> values)
            {
                Underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
                Values = values;
            }

            public override IReplicatedData Merge(IReplicatedData other)
            {
                PutDeltaOperation put;
                if (other is UpdateDeltaOperation update)
                {
                    var builder = Values.ToBuilder();
                    foreach (var entry in update.Values)
                    {
                        if (Values.TryGetValue(entry.Key, out var value))
                        {
                            builder[entry.Key] = value.Merge(entry.Value);
                        }
                        else
                        {
                            builder.Add(entry);
                        }
                    }
                    return new UpdateDeltaOperation(
                        underlying: (ORSet<TKey>.IDeltaOperation)Underlying.Merge(update.Underlying),
                        values: builder.ToImmutable());
                }
                else if ((put = other as PutDeltaOperation) != null && this.Values.Count == 1 && this.Values.ContainsKey(put.Key))
                {
                    return new PutDeltaOperation((ORSet<TKey>.IDeltaOperation)this.Underlying.Merge(put.Underlying), put.Key, put.Value);
                }
                else if (other is AtomicDeltaOperation)
                {
                    return new DeltaGroup(ImmutableArray.Create(this, (IDeltaOperation)other));
                }
                else
                {
                    var builder = ImmutableArray<IDeltaOperation>.Empty.ToBuilder();
                    builder.Add(this);
                    builder.AddRange(((DeltaGroup)other).Operations);
                    return new DeltaGroup(builder.ToImmutable());
                }
            }

            public override bool Equals(IDeltaOperation op)
            {
                if (ReferenceEquals(null, op)) return false;
                if (ReferenceEquals(this, op)) return true;
                if (op is UpdateDeltaOperation update)
                {
                    return Underlying.Equals(update.Underlying) && Values.SequenceEqual(update.Values);
                }
                return false;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = Underlying.GetHashCode();
                    foreach (var data in Values)
                    {
                        hash = (hash * 397) ^ data.Key.GetHashCode();
                        hash = (hash * 397) ^ data.Value.GetHashCode();
                    }
                    return hash;
                }
            }
        }

        internal sealed class RemoveDeltaOperation : AtomicDeltaOperation, ORDictionary.IRemoveDeltaOp
        {
            public RemoveDeltaOperation(ORSet<TKey>.IDeltaOperation underlying)
            {
                Underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
            }

            public override ORSet<TKey>.IDeltaOperation Underlying { get; }

            public override bool Equals(IDeltaOperation op)
            {
                if (ReferenceEquals(null, op)) return false;
                if (ReferenceEquals(this, op)) return true;
                if (op is RemoveDeltaOperation remove)
                {
                    return Underlying.Equals(remove.Underlying);
                }
                return false;
            }

            public override int GetHashCode() => Underlying.GetHashCode();
        }

        internal sealed class RemoveKeyDeltaOperation : AtomicDeltaOperation, ORDictionary.IRemoveKeyDeltaOp
        {
            public override ORSet<TKey>.IDeltaOperation Underlying { get; }
            public TKey Key { get; }

            public RemoveKeyDeltaOperation(ORSet<TKey>.IDeltaOperation underlying, TKey key)
            {
                Underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
                Key = key;
            }

            public override bool Equals(IDeltaOperation op)
            {
                if (ReferenceEquals(null, op)) return false;
                if (ReferenceEquals(this, op)) return true;
                if (op is RemoveKeyDeltaOperation remove)
                {
                    return Equals(Key, remove.Key) && Underlying.Equals(remove.Underlying);
                }
                return false;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = Underlying.GetHashCode();
                    hash = (hash * 397) ^ Key.GetHashCode();
                    return hash;
                }
            }
        }

        internal sealed class DeltaGroup : IDeltaOperation, IReplicatedDeltaSize, ORDictionary.IDeltaGroupOp
        {
            public readonly IDeltaOperation[] Operations;

            public DeltaGroup(IEnumerable<IDeltaOperation> operations)
            {
                this.Operations = operations.ToArray();
            }

            public IReplicatedData Merge(IReplicatedData other)
            {
                if (other is AtomicDeltaOperation atomic)
                {
                    var lastIndex = Operations.Length - 1;
                    var last = Operations[lastIndex];
                    if (last is PutDeltaOperation || last is UpdateDeltaOperation)
                    {
                        var builder = Operations.ToList();
                        var merged = (IDeltaOperation)last.Merge(atomic);
                        if (merged is AtomicDeltaOperation)
                        {
                            builder[lastIndex] = merged;
                            return new DeltaGroup(builder);
                        }
                        else
                        {
                            builder.RemoveAt(lastIndex);
                            builder.AddRange(((DeltaGroup)merged).Operations);
                            return new DeltaGroup(builder);
                        }
                    }
                    else
                    {
                        return new DeltaGroup(Operations.Union(new[]{atomic}));
                    }
                }
                else
                {
                    var group = (DeltaGroup)other;
                    return new DeltaGroup(this.Operations.Union(group.Operations));
                }
            }

            public IDeltaReplicatedData Zero => ((IReplicatedDelta)Operations.FirstOrDefault())?.Zero;
            public int DeltaSize => Operations.Length;

            public override bool Equals(object obj) => obj is IDeltaOperation operation && Equals(operation);

            public bool Equals(IDeltaOperation op)
            {
                if (ReferenceEquals(null, op)) return false;
                if (ReferenceEquals(this, op)) return true;
                if (op is DeltaGroup group)
                {
                    return Operations.SequenceEqual(group.Operations);
                }
                return false;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 0;
                    foreach (var op in Operations)
                    {
                        hash = (hash * 397) ^ op.GetHashCode();
                    }
                    return hash;
                }
            }

            public IReadOnlyList<ORDictionary.IDeltaOperation> OperationsSerialization => Operations.Cast<ORDictionary.IDeltaOperation>().ToList();
            public Type KeyType { get; } = typeof(TKey);
            public Type ValueType { get; } = typeof(TValue);
        }

        [NonSerialized]
        private readonly IDeltaOperation _syncRoot; //HACK: we need to ignore this field during serialization. This is the only way to do so on Hyperion on .NET Core
        public IDeltaOperation Delta => _syncRoot;

        public ORDictionary<TKey, TValue> ResetDelta()
        {
            return _syncRoot == null ? this : new ORDictionary<TKey, TValue>(KeySet.ResetDelta(), ValueMap);
        }

        public ORDictionary<TKey, TValue> MergeDelta(IDeltaOperation delta)
        {
            if (delta == null) throw new ArgumentNullException();

            var withDeltas = DryMergeDeltas(delta);
            return Merge(withDeltas);
        }

        private ORDictionary<TKey, TValue> DryMergeDeltas(IDeltaOperation delta, bool withValueDelta = false)
        {
            var mergedKeys = KeySet;
            var mergedValues = ImmutableDictionary<TKey, TValue>.Empty.ToBuilder();
            var tombstonedValues = ImmutableDictionary<TKey, TValue>.Empty.ToBuilder();
            foreach (var entry in ValueMap)
            {
                if (this.KeySet.Contains(entry.Key))
                    mergedValues.Add(entry);
                else
                    tombstonedValues.Add(entry);
            }

            if(!ProcessDelta(delta, mergedValues, tombstonedValues, ref mergedKeys))
                ProcessNestedDelta(delta, mergedValues, tombstonedValues, ref mergedKeys);

            if (withValueDelta)
            {
                foreach (var entry in mergedValues)
                {
                    // AddRange won't work in the face of repeated entries
                    tombstonedValues[entry.Key] = entry.Value;
                }
                return new ORDictionary<TKey, TValue>(mergedKeys, tombstonedValues.ToImmutable());
            }
            else
            {
                return new ORDictionary<TKey, TValue>(mergedKeys, mergedValues.ToImmutable());
            }
        }

        private bool ProcessDelta(IDeltaOperation delta, ImmutableDictionary<TKey, TValue>.Builder mergedValues, ImmutableDictionary<TKey, TValue>.Builder tombstonedValues, ref ORSet<TKey> mergedKeys)
        {
            switch (delta)
            {
                case PutDeltaOperation putOp:
                    mergedKeys = mergedKeys.MergeDelta(putOp.Underlying);
                    mergedValues[putOp.Key] = putOp.Value; // put is destructive and propagates only full values of B!
                    return true;
                case RemoveDeltaOperation removeOp:
                    if (removeOp.Underlying is ORSet<TKey>.RemoveDeltaOperation op)
                    {
                        // if op is RemoveDeltaOp then it must have exactly one element in the elements
                        var removedKey = op.Underlying.Elements.First();
                        mergedValues.Remove(removedKey);
                        mergedKeys = mergedKeys.MergeDelta(removeOp.Underlying);
                        // please note that if RemoveDeltaOp is not preceded by update clearing the value
                        // anomalies may result
                    }
                    else throw new ArgumentException("ORDictionary.RemoveDeltaOp must contain ORSet.RemoveDeltaOp inside");
                    return true; 
                case RemoveKeyDeltaOperation removeKeyOp:
                    // removeKeyOp tombstones values for later use
                    if (mergedValues.ContainsKey(removeKeyOp.Key))
                        tombstonedValues[removeKeyOp.Key] = mergedValues[removeKeyOp.Key];
                    mergedValues.Remove(removeKeyOp.Key);
                    mergedKeys = mergedKeys.MergeDelta(removeKeyOp.Underlying);
                    return true;
                case UpdateDeltaOperation updateOp:
                    mergedKeys = mergedKeys.MergeDelta(updateOp.Underlying);
                    foreach (var entry in updateOp.Values)
                    {
                        var key = entry.Key;
                        if (mergedKeys.Contains(key))
                        {
                            if (mergedValues.TryGetValue(key, out var value))
                                mergedValues[key] = MergeValue(value, entry.Value);
                            else if (tombstonedValues.TryGetValue(key, out value))
                                mergedValues[key] = MergeValue(value, entry.Value);
                            else
                            {
                                if (entry.Value is IReplicatedDelta v) mergedValues[key] = MergeValue((TValue)v.Zero, entry.Value);
                                else mergedValues[key] = (TValue)entry.Value;
                            }
                        }
                    }
                    return true;
                default: return false;
            }
        }

        private bool ProcessNestedDelta(IDeltaOperation delta, ImmutableDictionary<TKey, TValue>.Builder mergedValues, ImmutableDictionary<TKey, TValue>.Builder tombstonedValues, ref ORSet<TKey> mergedKeys)
        {
            if (delta is DeltaGroup ops)
            {
                foreach (var op in ops.Operations)
                {
                    if(!ProcessDelta(op, mergedValues, tombstonedValues, ref mergedKeys))
                        throw new IllegalStateException("Cannot nest DeltaGroup");
                }
                return true;
            }
            else return false;
        }

        private TValue MergeValue(TValue value, IReplicatedData delta)
        {
            if (value is IDeltaReplicatedData v && delta is IReplicatedDelta d)
            {
                return (TValue)v.MergeDelta(d);
            }
            else
            {
                return (TValue)value.Merge(delta);
            }
        }

        private IDeltaOperation NewDelta(IDeltaOperation delta) =>
            Delta == null ? delta : (IDeltaOperation)Delta.Merge(delta);

        #endregion

        internal ORDictionary<TKey, TValue> MergeRetainingDeletedValues(ORDictionary<TKey, TValue> other)
        {
            var mergedKeys = KeySet.Merge(other.KeySet);
            return DryMerge(other, mergedKeys, ValueMap.Keys.Union(other.ValueMap.Keys).GetEnumerator());
        }

        internal ORDictionary<TKey, TValue> RemoveKey(UniqueAddress node, TKey key)
        {
            var newKeys = KeySet.ResetDelta().Remove(node, key);
            var removeKeyDeltaOp = new RemoveKeyDeltaOperation(newKeys.Delta, key);
            return new ORDictionary<TKey, TValue>(newKeys, ValueMap, NewDelta(removeKeyDeltaOp));
        }

        internal ORDictionary<TKey, TValue> MergeDeltaRetainingDeletedValues(IDeltaOperation delta)
        {
            var withDeltas = DryMergeDeltas(delta, true);
            return MergeRetainingDeletedValues(withDeltas);
        }

        public Type KeyType { get; } = typeof(TKey);
        public Type ValueType { get; } = typeof(TValue);
    }
}
