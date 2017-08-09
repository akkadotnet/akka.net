//-----------------------------------------------------------------------
// <copyright file="LWWDictionary.cs" company="Akka.NET Project">
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
using Akka.Cluster;
using Akka.Util.Internal;

namespace Akka.DistributedData
{
    /// <summary>
    /// Typed key used to store <see cref="LWWDictionary{TKey,TValue}"/> replica 
    /// inside current <see cref="Replicator"/> key-value store.
    /// </summary>
    /// <typeparam name="TKey">Type of a key used by corresponding <see cref="LWWDictionary{TKey,TValue}"/>.</typeparam>
    /// <typeparam name="TValue">Type of a value used by corresponding <see cref="LWWDictionary{TKey,TValue}"/>.</typeparam>
    [Serializable]
    public sealed class LWWDictionaryKey<TKey, TValue> : Key<LWWDictionary<TKey, TValue>>
    {
        /// <summary>
        /// Creates a new instance of a <see cref="LWWDictionaryKey{TKey,TValue}"/> with provided key identifier.
        /// </summary>
        /// <param name="id">Identifier used to find corresponding <see cref="LWWDictionary{TKey,TValue}"/>.</param>
        public LWWDictionaryKey(string id) : base(id) { }
    }

    /// <summary>
    /// A static class with various constructor methods for <see cref="LWWDictionary{TKey,TValue}"/>.
    /// </summary>
    public static class LWWDictionary
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TValue">TBD</typeparam>
        /// <param name="node">TBD</param>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        /// <param name="clock">TBD</param>
        /// <returns>TBD</returns>
        public static LWWDictionary<TKey, TValue> Create<TKey, TValue>(UniqueAddress node, TKey key, TValue value, Clock<TValue> clock = null) =>
            LWWDictionary<TKey, TValue>.Empty.SetItem(node, key, value, clock);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TValue">TBD</typeparam>
        /// <param name="elements">TBD</param>
        /// <returns>TBD</returns>
        public static LWWDictionary<TKey, TValue> Create<TKey, TValue>(params Tuple<UniqueAddress, TKey, TValue>[] elements) =>
            elements.Aggregate(LWWDictionary<TKey, TValue>.Empty, (dictionary, t) => dictionary.SetItem(t.Item1, t.Item2, t.Item3));

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TValue">TBD</typeparam>
        /// <param name="elements">TBD</param>
        /// <param name="clock">TBD</param>
        /// <returns>TBD</returns>
        public static LWWDictionary<TKey, TValue> Create<TKey, TValue>(IEnumerable<Tuple<UniqueAddress, TKey, TValue>> elements, Clock<TValue> clock = null) =>
            elements.Aggregate(LWWDictionary<TKey, TValue>.Empty, (dictionary, t) => dictionary.SetItem(t.Item1, t.Item2, t.Item3, clock));
    }

    /// <summary>
    /// Specialized <see cref="LWWDictionary{TKey, TValue}"/> with <see cref="LWWRegister{T}"/> values.
    /// 
    /// <see cref="LWWRegister{T}"/> relies on synchronized clocks and should only be used when the choice of
    /// value is not important for concurrent updates occurring within the clock skew.
    /// 
    /// Instead of using timestamps based on DateTime.UtcNow.Ticks time it is possible to
    /// use a timestamp value based on something else, for example an increasing version number
    /// from a database record that is used for optimistic concurrency control.
    /// 
    /// For first-write-wins semantics you can use the <see cref="LWWRegister{T}.ReverseClock"/> instead of the
    /// <see cref="LWWRegister{T}.DefaultClock"/>
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    /// <typeparam name="TKey">TBD</typeparam>
    /// <typeparam name="TValue">TBD</typeparam>
    [Serializable]
    public sealed partial class LWWDictionary<TKey, TValue> :
        IDeltaReplicatedData<LWWDictionary<TKey, TValue>, ORDictionary<TKey, LWWRegister<TValue>>.IDeltaOperation>,
        IRemovedNodePruning<LWWDictionary<TKey, TValue>>,
        IReplicatedDataSerialization,
        IEquatable<LWWDictionary<TKey, TValue>>,
        IEnumerable<KeyValuePair<TKey, TValue>>
    {
        /// <summary>
        /// An empty instance of the <see cref="LWWDictionary{TKey,TValue}"/>
        /// </summary>
        public static readonly LWWDictionary<TKey, TValue> Empty = new LWWDictionary<TKey, TValue>(ORDictionary<TKey, LWWRegister<TValue>>.Empty);

        private readonly ORDictionary<TKey, LWWRegister<TValue>> _underlying;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="underlying">TBD</param>
        public LWWDictionary(ORDictionary<TKey, LWWRegister<TValue>> underlying)
        {
            _underlying = underlying;
        }

        /// <summary>
        /// Returns all entries stored within current <see cref="LWWDictionary{TKey,TValue}"/>
        /// </summary>
        public IImmutableDictionary<TKey, TValue> Entries => _underlying.Entries
            .Select(kv => new KeyValuePair<TKey, TValue>(kv.Key, kv.Value.Value))
            .ToImmutableDictionary();

        /// <summary>
        /// Returns collection of keys stored within current <see cref="LWWDictionary{TKey,TValue}"/>.
        /// </summary>
        public IEnumerable<TKey> Keys => _underlying.Keys;

        /// <summary>
        /// Returns collection of values stored within current <see cref="LWWDictionary{TKey,TValue}"/>.
        /// </summary>
        public IEnumerable<TValue> Values => _underlying.Values.Select(x => x.Value);

        /// <summary>
        /// Returns value stored under provided <paramref name="key"/>.
        /// </summary>
        /// <param name="key">TBD</param>
        public TValue this[TKey key] => _underlying[key].Value;

        /// <summary>
        /// Determines current <see cref="LWWDictionary{TKey,TValue}"/> contains entry with provided <paramref name="key"/>.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <returns>TBD</returns>
        public bool ContainsKey(TKey key) => _underlying.ContainsKey(key);

        /// <summary>
        /// Determines if current <see cref="LWWDictionary{TKey,TValue}"/> is empty.
        /// </summary>
        public bool IsEmpty => _underlying.IsEmpty;

        /// <summary>
        /// Returns number of entries stored within current <see cref="LWWDictionary{TKey,TValue}"/>.
        /// </summary>
        public int Count => _underlying.Count;

        /// <summary>
        /// Adds an entry to the map.
        /// 
        /// You can provide your <paramref name="clock"/> implementation instead of using timestamps based
        /// on DateTime.UtcNow.Ticks time. The timestamp can for example be an
        /// increasing version number from a database record that is used for optimistic
        /// concurrency control.
        /// </summary>
        public LWWDictionary<TKey, TValue> SetItem(Cluster.Cluster node, TKey key, TValue value,
            Clock<TValue> clock = null) => SetItem(node.SelfUniqueAddress, key, value, clock);

        /// <summary>
        /// Adds an entry to the map.
        /// 
        /// You can provide your <paramref name="clock"/> implementation instead of using timestamps based
        /// on DateTime.UtcNow.Ticks time. The timestamp can for example be an
        /// increasing version number from a database record that is used for optimistic
        /// concurrency control.
        /// </summary>
        public LWWDictionary<TKey, TValue> SetItem(UniqueAddress node, TKey key, TValue value,
            Clock<TValue> clock = null)
        {
            LWWRegister<TValue> register;
            var newRegister = _underlying.TryGetValue(key, out register)
                ? register.WithValue(node, value, clock ?? LWWRegister<TValue>.DefaultClock)
                : new LWWRegister<TValue>(node, value, clock ?? LWWRegister<TValue>.DefaultClock);

            return new LWWDictionary<TKey, TValue>(_underlying.SetItem(node, key, newRegister));
        }

        /// <summary>
        /// Removes an entry from the map.
        /// Note that if there is a conflicting update on another node the entry will
        /// not be removed after merge.
        /// </summary>
        public LWWDictionary<TKey, TValue> Remove(Cluster.Cluster node, TKey key) => Remove(node.SelfUniqueAddress, key);

        /// <summary>
        /// Removes an entry from the map.
        /// Note that if there is a conflicting update on another node the entry will
        /// not be removed after merge.
        /// </summary>
        public LWWDictionary<TKey, TValue> Remove(UniqueAddress node, TKey key) =>
            new LWWDictionary<TKey, TValue>(_underlying.Remove(node, key));

        /// <summary>
        /// Tries to return a value under provided <paramref name="key"/> is such value exists.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetValue(TKey key, out TValue value)
        {
            LWWRegister<TValue> register;
            if (_underlying.TryGetValue(key, out register))
            {
                value = register.Value;
                return true;
            }

            value = default(TValue);
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public LWWDictionary<TKey, TValue> Merge(LWWDictionary<TKey, TValue> other) =>
            new LWWDictionary<TKey, TValue>(_underlying.Merge(other._underlying));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public IReplicatedData Merge(IReplicatedData other) =>
            Merge((LWWDictionary<TKey, TValue>)other);

        public ImmutableHashSet<UniqueAddress> ModifiedByNodes => _underlying.ModifiedByNodes;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedNode">TBD</param>
        /// <returns>TBD</returns>
        public bool NeedPruningFrom(UniqueAddress removedNode) =>
            _underlying.NeedPruningFrom(removedNode);

        IReplicatedData IRemovedNodePruning.PruningCleanup(UniqueAddress removedNode) => PruningCleanup(removedNode);

        IReplicatedData IRemovedNodePruning.Prune(UniqueAddress removedNode, UniqueAddress collapseInto) => Prune(removedNode, collapseInto);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedNode">TBD</param>
        /// <param name="collapseInto">TBD</param>
        /// <returns>TBD</returns>
        public LWWDictionary<TKey, TValue> Prune(UniqueAddress removedNode, UniqueAddress collapseInto) =>
            new LWWDictionary<TKey, TValue>(_underlying.Prune(removedNode, collapseInto));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedNode">TBD</param>
        /// <returns>TBD</returns>
        public LWWDictionary<TKey, TValue> PruningCleanup(UniqueAddress removedNode) =>
            new LWWDictionary<TKey, TValue>(_underlying.PruningCleanup(removedNode));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(LWWDictionary<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return _underlying.Equals(other._underlying);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() =>
            _underlying.Select(x => new KeyValuePair<TKey, TValue>(x.Key, x.Value.Value)).GetEnumerator();

        /// <inheritdoc/>
        public override bool Equals(object obj) =>
            obj is LWWDictionary<TKey, TValue> && Equals((LWWDictionary<TKey, TValue>)obj);

        /// <inheritdoc/>
        public override int GetHashCode() => _underlying.GetHashCode();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc/>
        public override string ToString()
        {
            var sb = new StringBuilder("LWWDictionary(");
            sb.AppendJoin(", ", Entries);
            sb.Append(')');
            return sb.ToString();
        }

        public ORDictionary<TKey, LWWRegister<TValue>>.IDeltaOperation Delta => _underlying.Delta;

        IReplicatedDelta IDeltaReplicatedData.Delta => Delta;

        IReplicatedData IDeltaReplicatedData.MergeDelta(IReplicatedDelta delta) =>
            MergeDelta((ORDictionary<TKey, LWWRegister<TValue>>.IDeltaOperation)delta);

        IReplicatedData IDeltaReplicatedData.ResetDelta() => ResetDelta();

        public LWWDictionary<TKey, TValue> MergeDelta(ORDictionary<TKey, LWWRegister<TValue>>.IDeltaOperation delta) =>
            new LWWDictionary<TKey, TValue>(_underlying.MergeDelta(delta));

        public LWWDictionary<TKey, TValue> ResetDelta() =>
            new LWWDictionary<TKey, TValue>(_underlying.ResetDelta());
    }
}