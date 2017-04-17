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
    public sealed class ORDictionaryKey<TKey, TValue> : Key<ORDictionary<TKey, TValue>> where TValue : IReplicatedData<TValue>
    {
        public ORDictionaryKey(string id) : base(id) { }
    }

    public static class ORDictionary
    {
        public static ORDictionary<TKey, TValue> Create<TKey, TValue>(UniqueAddress node, TKey key, TValue value) where TValue : IReplicatedData<TValue> =>
            ORDictionary<TKey, TValue>.Empty.SetItem(node, key, value);

        public static ORDictionary<TKey, TValue> Create<TKey, TValue>(params Tuple<UniqueAddress, TKey, TValue>[] elements) where TValue : IReplicatedData<TValue> =>
            elements.Aggregate(ORDictionary<TKey, TValue>.Empty, (acc, t) => acc.SetItem(t.Item1, t.Item2, t.Item3));

        public static ORDictionary<TKey, TValue> Create<TKey, TValue>(IEnumerable<Tuple<UniqueAddress, TKey, TValue>> elements) where TValue : IReplicatedData<TValue> =>
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
    public class ORDictionary<TKey, TValue> :
        IEnumerable<KeyValuePair<TKey, TValue>>,
        IRemovedNodePruning<ORDictionary<TKey, TValue>>,
        IEquatable<ORDictionary<TKey, TValue>>,
        IReplicatedDataSerialization,
        IDeltaReplicatedData<ORDictionary<TKey, TValue>, ORDictionary<TKey, TValue>.IDeltaOperation>
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
            _delta = delta;
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
        /// Consider using <see cref="AddOrUpdate"/> instead of <see cref="SetItem(Akka.Cluster.Cluster,TKey,TValue)"/> if you want modify
        /// existing entry.
        /// 
        /// <see cref="ArgumentException"/> is thrown if you try to replace an existing <see cref="ORSet{T}"/>
        /// value, because important history can be lost when replacing the `ORSet` and
        /// undesired effects of merging will occur. Use <see cref="ORMultiValueDictionary{TKey,TValue}"/> or <see cref="AddOrUpdate"/> instead.
        /// </summary>
        public ORDictionary<TKey, TValue> SetItem(Cluster.Cluster node, TKey key, TValue value) =>
            SetItem(node.SelfUniqueAddress, key, value);

        /// <summary>
        /// Adds an entry to the map.
        /// Note that the new <paramref name="value"/> will be merged with existing values
        /// on other nodes and the outcome depends on what <see cref="IReplicatedData"/>
        /// type that is used.
        /// 
        /// Consider using <see cref="AddOrUpdate"/> instead of <see cref="SetItem(UniqueAddress,TKey,TValue)"/> if you want modify
        /// existing entry.
        /// 
        /// <see cref="ArgumentException"/> is thrown if you try to replace an existing <see cref="ORSet{T}"/>
        /// value, because important history can be lost when replacing the `ORSet` and
        /// undesired effects of merging will occur. Use <see cref="ORMultiValueDictionary{TKey,TValue}"/> or <see cref="AddOrUpdate"/> instead.
        /// </summary>
        public ORDictionary<TKey, TValue> SetItem(UniqueAddress node, TKey key, TValue value)
        {
            if (value is IORSet && ValueMap.ContainsKey(key))
                throw new ArgumentException("ORDictionary.SetItems may not be used to replace an existing ORSet", nameof(value));

            var delta = new PutDeltaOperation(KeySet.ResetDelta().Add(node, key).Delta, new KeyValuePair<TKey, TValue>(key, value));
            // put forcibly damages history, so we propagate full value that will overwrite previous values
            return new ORDictionary<TKey, TValue>(KeySet.Add(node, key), ValueMap.SetItem(key, value), NewDelta(delta));
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
            TValue oldValue;
            bool hasOldValue;
            if (!ValueMap.TryGetValue(key, out oldValue))
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
            var newValue = modify(oldValue);
            if (valueDeltas && oldValue is IDeltaReplicatedData)
            {
                var newValueDelta = ((IDeltaReplicatedData) newValue).Delta;
                if (newValueDelta != null && hasOldValue)
                {
                    var op = new UpdateDeltaOperation(newKeys.Delta, ImmutableDictionary.CreateRange(new [] { new KeyValuePair<TKey, IReplicatedData>(key, newValueDelta) }));
                    return new ORDictionary<TKey, TValue>(newKeys, ValueMap.SetItem(key, newValue), NewDelta(op));
                }
                else
                {
                    var op = new PutDeltaOperation(newKeys.Delta, new KeyValuePair<TKey, TValue>(key, newValue));
                    return new ORDictionary<TKey, TValue>(newKeys, ValueMap.SetItem(key, newValue), NewDelta(op));
                }
            }
            else
            {
                var delta = new PutDeltaOperation(KeySet.ResetDelta().Add(node, key).Delta, new KeyValuePair<TKey, TValue>(key, newValue));
                return new ORDictionary<TKey, TValue>(KeySet.Add(node, key), ValueMap.SetItem(key, newValue), NewDelta(delta));
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
        public ORDictionary<TKey, TValue> Remove(UniqueAddress node, TKey key) =>
            new ORDictionary<TKey, TValue>(KeySet.Remove(node, key), ValueMap.Remove(key));

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
                TValue value1, value2;
                if (this.ValueMap.TryGetValue(key, out value1))
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

        IReplicatedData IDeltaReplicatedData.MergeDelta(IReplicatedDelta delta) => Merge((IDeltaOperation) delta);

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
                    ? acc.SetItem(kv.Key, (TValue)data.Prune(removedNode, collapseInto))
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
            return obj is ORDictionary<TKey, TValue> && Equals((ORDictionary<TKey, TValue>)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((KeySet.GetHashCode() * 397) ^ ValueMap.GetHashCode());
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

        #region delta operations

        public interface IDeltaOperation : IReplicatedDelta, IRequireCausualDeliveryOfDeltas
        {
        }

        internal abstract class AtomicDeltaOperation : IDeltaOperation
        {
            public abstract ORSet<TKey>.IDeltaOperation Underlying { get; }
            public virtual IReplicatedData Merge(IReplicatedData other)
            {
                if (other is AtomicDeltaOperation)
                    return new DeltaGroup(ImmutableArray.Create(this, (IDeltaOperation) other));
                else
                {
                    var builder = ImmutableArray<IDeltaOperation>.Empty.ToBuilder();
                    builder.Add(this);
                    builder.AddRange(((DeltaGroup)other).Operations);
                    return new DeltaGroup(builder.ToImmutable());
                }
            }

            public IDeltaReplicatedData Zero => ORDictionary<TKey, TValue>.Empty;
        }

        internal sealed class PutDeltaOperation : AtomicDeltaOperation
        {
            public override ORSet<TKey>.IDeltaOperation Underlying { get; }
            public KeyValuePair<TKey, TValue> Entry { get; }

            public PutDeltaOperation(ORSet<TKey>.IDeltaOperation underlying, KeyValuePair<TKey, TValue> entry)
            {
                Underlying = underlying;
                Entry = entry;
            }

            public override IReplicatedData Merge(IReplicatedData other)
            {
                UpdateDeltaOperation update;
                var put = other as PutDeltaOperation;
                if (put != null && Equals(this.Entry.Key, put.Entry.Key))
                {
                    return new PutDeltaOperation((ORSet<TKey>.IDeltaOperation)this.Underlying.Merge(put.Underlying), put.Entry);
                }
                else if ((update = other as UpdateDeltaOperation) != null && update.Values.Count == 1 && update.Values.ContainsKey(this.Entry.Key))
                {
                    var merged = (ORSet<TKey>.IDeltaOperation) this.Underlying.Merge(update.Underlying);
                    var e2 = update.Values.First().Value;
                    if (this.Entry.Value is IDeltaReplicatedData)
                    {
                        var mergedDelta = ((IDeltaReplicatedData)this.Entry.Value).MergeDelta((IReplicatedDelta)e2);
                        return new PutDeltaOperation(merged, new KeyValuePair<TKey, TValue>(this.Entry.Key, (TValue)mergedDelta));
                    }
                    else
                    {
                        var mergedDelta = this.Entry.Value.Merge(e2);
                        return new PutDeltaOperation(merged, new KeyValuePair<TKey, TValue>(this.Entry.Key, (TValue)mergedDelta));
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
        }

        internal sealed class UpdateDeltaOperation : AtomicDeltaOperation
        {
            public override ORSet<TKey>.IDeltaOperation Underlying { get; }
            public ImmutableDictionary<TKey, IReplicatedData> Values { get; }

            public UpdateDeltaOperation(ORSet<TKey>.IDeltaOperation underlying, ImmutableDictionary<TKey, IReplicatedData> values)
            {
                Underlying = underlying;
                Values = values;
            }

            public override IReplicatedData Merge(IReplicatedData other)
            {
                PutDeltaOperation put;
                if (other is UpdateDeltaOperation)
                {
                    var update = (UpdateDeltaOperation)other;
                    var builder = this.Values.ToBuilder();
                    foreach (var entry in update.Values)
                    {
                        IReplicatedData value;
                        if (this.Values.TryGetValue(entry.Key, out value))
                        {
                            builder.Add(entry.Key, value.Merge(entry.Value));
                        }
                        else
                        {
                            builder.Add(entry);
                        }
                    }
                    return new UpdateDeltaOperation(
                        underlying: (ORSet<TKey>.IDeltaOperation)this.Underlying.Merge(update),
                        values: builder.ToImmutable());
                }
                else if ((put = other as PutDeltaOperation) != null && this.Values.Count == 1 && this.Values.ContainsKey(put.Entry.Key))
                {
                    return new PutDeltaOperation((ORSet<TKey>.IDeltaOperation)this.Underlying.Merge(put.Underlying), put.Entry);
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
        }

        internal sealed class RemoveDeltaOperation : AtomicDeltaOperation
        {
            public RemoveDeltaOperation(ORSet<TKey>.IDeltaOperation underlying)
            {
                Underlying = underlying;
            }

            public override ORSet<TKey>.IDeltaOperation Underlying { get; }
        }

        internal sealed class RemoveKeyDeltaOperation : AtomicDeltaOperation
        {
            public override ORSet<TKey>.IDeltaOperation Underlying { get; }
            public TKey Key { get; }

            public RemoveKeyDeltaOperation(ORSet<TKey>.IDeltaOperation underlying, TKey key)
            {
                Underlying = underlying;
                Key = key;
            }
        }

        internal sealed class DeltaGroup : IDeltaOperation
        {
            public readonly ImmutableArray<IDeltaOperation> Operations;

            public DeltaGroup(ImmutableArray<IDeltaOperation> operations)
            {
                this.Operations = operations;
            }

            public IReplicatedData Merge(IReplicatedData other)
            {
                var atomic = other as AtomicDeltaOperation;
                if (atomic != null)
                {
                    var lastIndex = Operations.Length - 1;
                    var last = Operations[lastIndex];
                    if (last is PutDeltaOperation || last is UpdateDeltaOperation)
                    {
                        var builder = this.Operations.ToBuilder();
                        var merged = (IDeltaOperation)last.Merge(atomic);
                        if (merged is AtomicDeltaOperation)
                        {
                            builder[lastIndex] = merged;
                            return new DeltaGroup(builder.ToImmutable());
                        }
                        else
                        {
                            builder.RemoveAt(lastIndex);
                            builder.AddRange(((DeltaGroup)merged).Operations);
                            return new DeltaGroup(builder.ToImmutable());
                        }
                    }
                    else
                    {
                        return new DeltaGroup(Operations.Add(atomic));
                    }
                }
                else
                {
                    var group = (DeltaGroup) other;
                    return new DeltaGroup(this.Operations.AddRange(group.Operations));
                }
            }

            public IDeltaReplicatedData Zero => ((IReplicatedDelta)Operations.FirstOrDefault())?.Zero;
        }

        [NonSerialized]
        private readonly IDeltaOperation _delta;
        public IDeltaOperation Delta => _delta;

        public ORDictionary<TKey, TValue> ResetDelta()
        {
            return _delta == null ? this : new ORDictionary<TKey, TValue>(KeySet.ResetDelta(), ValueMap);
        }

        public ORDictionary<TKey, TValue> MergeDelta(IDeltaOperation delta)
        {
            var withDeltas = DryMergeDeltas(delta);
            return this.Merge(withDeltas);
        }

        private ORDictionary<TKey, TValue> DryMergeDeltas(IDeltaOperation delta, bool withValueDelta = false)
        {
            var mergedKeys = this.KeySet;
            var mergedValues = ImmutableDictionary<TKey, TValue>.Empty.ToBuilder();
            var tombstonedValues = ImmutableDictionary<TKey, TValue>.Empty.ToBuilder();
            foreach (var entry in this.ValueMap)
            {
                if (mergedKeys.Contains(entry.Key))
                    mergedValues.Add(entry);
                else
                    tombstonedValues.Add(entry);
            }

            ProcessDelta(delta, mergedValues, tombstonedValues, ref mergedKeys, true);

            if (withValueDelta)
            {
                tombstonedValues.AddRange(mergedValues);
                return new ORDictionary<TKey, TValue>(mergedKeys, tombstonedValues.ToImmutable());
            }
            else
            {
                return new ORDictionary<TKey, TValue>(mergedKeys, mergedValues.ToImmutable());
            }
        }

        private void ProcessDelta(IDeltaOperation delta, ImmutableDictionary<TKey, TValue>.Builder mergedValues, ImmutableDictionary<TKey, TValue>.Builder tombstonedValues, ref ORSet<TKey> mergedKeys, bool groupAllowed = true)
        {
            if (delta is PutDeltaOperation)
            {
                var op = (PutDeltaOperation) delta;
                mergedKeys = mergedKeys.MergeDelta(op.Underlying);
                mergedValues.Add(op.Entry); // put is destructive and propagates only full values of B!
            }
            else if (delta is RemoveDeltaOperation)
            {
                var op = (RemoveDeltaOperation)delta;
                var nested = op.Underlying as ORSet<TKey>.RemoveDeltaOperation;
                if (nested != null)
                {
                    // if op is RemoveDeltaOp then it must have exactly one element in the elements
                    mergedValues.Remove(nested.Underlying.First());
                    mergedKeys = mergedKeys.MergeDelta(op.Underlying);
                    // please note that if RemoveDeltaOp is not preceded by update clearing the value
                    // anomalies may result
                }
                else
                {
                    throw new ArgumentException("ORMap.RemoveDeltaOp must contain ORSet.RemoveDeltaOp inside");   
                }
            }
            else if (delta is RemoveKeyDeltaOperation)
            {
                var op = (RemoveKeyDeltaOperation)delta;
                // removeKeyOp tombstones values for later use
                TValue value;
                if (mergedValues.TryGetValue(op.Key, out value))
                {
                    tombstonedValues.Add(op.Key, value);
                }

                mergedValues.Remove(op.Key);
                mergedKeys = mergedKeys.MergeDelta(op.Underlying);
            }
            else if (delta is UpdateDeltaOperation)
            {
                var op = (UpdateDeltaOperation)delta;
                mergedKeys = mergedKeys.MergeDelta(op.Underlying);
                foreach (var entry in op.Values)
                {
                    var key = entry.Key;
                    if (mergedKeys.Contains(key))
                    {
                        TValue value;
                        if (mergedValues.TryGetValue(key, out value))
                        {
                            mergedValues.Add(key, MergeValue(value, entry.Value));
                        }
                        else if (tombstonedValues.TryGetValue(key, out value))
                        {
                            mergedValues.Add(key, MergeValue(value, entry.Value));
                        }
                        else
                        {
                            var v = entry.Value as IReplicatedDelta;
                            if (v != null)
                            {
                                mergedValues.Add(key, MergeValue((TValue)v.Zero, entry.Value));
                            }
                            else
                            {
                                mergedValues.Add(key, (TValue)entry.Value);
                            }
                        }
                    }
                }
            }
            else if (delta is DeltaGroup && groupAllowed)
            {
                var ops = (DeltaGroup) delta;
                foreach (var op in ops.Operations)
                {
                    ProcessDelta(op, mergedValues, tombstonedValues, ref mergedKeys, false);
                }
            }
            else throw new NotSupportedException($"Unknown option {delta}");
        }

        private TValue MergeValue(TValue value, IReplicatedData delta)
        {
            var v = value as IDeltaReplicatedData;
            var d = delta as IReplicatedDelta;
            if (v != null && d != null)
            {
                return (TValue) v.MergeDelta(d);
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
            return DryMerge(other, mergedKeys, this.ValueMap.Keys.Union(other.ValueMap.Keys).GetEnumerator());
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
    }
}