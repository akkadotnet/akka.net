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
using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData
{
    [Serializable]
    public sealed class LWWDictionaryKey<TKey, TValue> : Key<LWWDictionary<TKey, TValue>>
    {
        public LWWDictionaryKey(string id) : base(id)
        {
        }
    }

    public static class LWWDictionary
    {
        public static LWWDictionary<TKey, TValue> Create<TKey, TValue>(UniqueAddress node, TKey key, TValue value, Clock<TValue> clock = null) =>
            LWWDictionary<TKey, TValue>.Empty.SetItem(node, key, value, clock);

        public static LWWDictionary<TKey, TValue> Create<TKey, TValue>(params Tuple<UniqueAddress, TKey, TValue>[] elements) =>
            elements.Aggregate(LWWDictionary<TKey, TValue>.Empty, (dictionary, t) => dictionary.SetItem(t.Item1, t.Item2, t.Item3));

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
    [Serializable]
    public sealed class LWWDictionary<TKey, TValue> : IReplicatedData<LWWDictionary<TKey, TValue>>,
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
        public TValue this[TKey key] => _underlying[key].Value;

        /// <summary>
        /// Determines current <see cref="LWWDictionary{TKey,TValue}"/> contains entry with provided <paramref name="key"/>.
        /// </summary>
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
        public LWWDictionary<TKey, TValue> Remove(UniqueAddress node, TKey key) => 
            new LWWDictionary<TKey, TValue>(_underlying.Remove(node, key));

        /// <summary>
        /// Tries to return a value under provided <paramref name="key"/> is such value exists.
        /// </summary>
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

        public LWWDictionary<TKey, TValue> Merge(LWWDictionary<TKey, TValue> other) => 
            new LWWDictionary<TKey, TValue>(_underlying.Merge(other._underlying));

        public IReplicatedData Merge(IReplicatedData other) => 
            Merge((LWWDictionary<TKey, TValue>) other);

        public bool NeedPruningFrom(UniqueAddress removedNode) => 
            _underlying.NeedPruningFrom(removedNode);

        public LWWDictionary<TKey, TValue> Prune(UniqueAddress removedNode, UniqueAddress collapseInto) => 
            new LWWDictionary<TKey, TValue>(_underlying.Prune(removedNode, collapseInto));

        public LWWDictionary<TKey, TValue> PruningCleanup(UniqueAddress removedNode) => 
            new LWWDictionary<TKey, TValue>(_underlying.PruningCleanup(removedNode));

        public bool Equals(LWWDictionary<TKey, TValue> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return _underlying.Equals(other._underlying);
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => 
            _underlying.Select(x => new KeyValuePair<TKey, TValue>(x.Key, x.Value.Value)).GetEnumerator();

        public override bool Equals(object obj) => 
            obj is LWWDictionary<TKey, TValue> && Equals((LWWDictionary<TKey, TValue>) obj);

        public override int GetHashCode() => _underlying.GetHashCode();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString()
        {
            var sb = new StringBuilder("LWWDictionary(");
            foreach (var entry in Entries)
            {
                sb.Append(entry.Key).Append("->").Append(entry.Value).Append(", ");
            }
            sb.Append(')');
            return sb.ToString();
        }
    }
}