using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData.Local
{

    /// <summary>
    /// A wrapper around <see cref="ORMultiDictionary{TKey,TValue}"/> that works in context of the current cluster.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TVal"></typeparam>
    public struct LocalORMultiDictionary<TKey, TVal> : ISurrogated, IEnumerable<KeyValuePair<TKey, IImmutableSet<TVal>>>
    {
        internal sealed class Surrogate : ISurrogate
        {
            private readonly ORMultiDictionary<TKey, TVal> _dictionary;

            public Surrogate(ORMultiDictionary<TKey, TVal> dictionary)
            {
                _dictionary = dictionary;
            }

            public ISurrogated FromSurrogate(ActorSystem system) =>
                new LocalORMultiDictionary<TKey, TVal>(Cluster.Cluster.Get(system), _dictionary);
        }

        private readonly UniqueAddress _currentNode;
        private readonly ORMultiDictionary<TKey, TVal> _crdt;

        internal LocalORMultiDictionary(UniqueAddress currentNode, ORMultiDictionary<TKey, TVal> crdt) : this()
        {
            _currentNode = currentNode;
            _crdt = crdt;
        }

        public LocalORMultiDictionary(Cluster.Cluster cluster, ORMultiDictionary<TKey, TVal> crdt) : this(cluster.SelfUniqueAddress, crdt)
        {
        }

        /// <summary>
        /// Returns collection of the elements inside the underlying ORMultiDictionary.
        /// </summary>
        public IImmutableDictionary<TKey, IImmutableSet<TVal>> Entries => _crdt.Entries;

        /// <summary>
        /// Returns all keys stored within underlying ORMultiDictionary.
        /// </summary>
        public IEnumerable<TKey> Keys => _crdt.Keys;

        /// <summary>
        /// Returns all values stored in all buckets within underlying ORMultiDictionary.
        /// </summary>
        public IEnumerable<TVal> Values => _crdt.Values;

        /// <summary>
        /// Returns number of elements inside the unterlying ORMultiDictionary.
        /// </summary>
        public int Count => _crdt.Count;

        /// <summary>
        /// Determines if underlying ORMultiDictionary is empty.
        /// </summary>
        public bool IsEmpty => _crdt.IsEmpty;

        /// <summary>
        /// Gets or sets provided key-valu of the underlying ORMultiDictionary within scope of the current cluster node.
        /// </summary>
        public IImmutableSet<TVal> this[TKey key] => _crdt[key];

        /// <summary>
        /// Gets value determining, if underlying ORMultiDictionary contains specified <paramref name="key"/>.
        /// </summary>
        public bool ContainsKey(TKey key) => _crdt.ContainsKey(key);

        /// <summary>
        /// Tries to retrieve element stored under provided <paramref name="key"/> in the underlying ORMultiDictionary,
        /// returning true if such value existed.
        /// </summary>
        public bool TryGetValue(TKey key, out IImmutableSet<TVal> value) => _crdt.TryGetValue(key, out value);

        /// <summary>
        /// Stored provided <paramref name="value"/> in entry with given <paramref name="key"/> inside the
        /// underlying ORMultiDictionary in scope of the current node, and returning new local dictionary in result.
        /// </summary>
        public LocalORMultiDictionary<TKey, TVal> SetItems(TKey key, IImmutableSet<TVal> value) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.SetItems(_currentNode, key, value));

        /// <summary>
        /// Adds provided <paramref name="value"/> into a bucket under provided <paramref name="key"/>
        /// within the context of the current cluster.
        /// </summary>
        public LocalORMultiDictionary<TKey, TVal> AddItem(TKey key, TVal value) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.AddItem(_currentNode, key, value));

        /// <summary>
        /// Removes provided <paramref name="value"/> from a bucket under provided <paramref name="key"/>
        /// within the context of the current cluster.
        /// </summary>
        public LocalORMultiDictionary<TKey, TVal> RemoveItem(TKey key, TVal value) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.RemoveItem(_currentNode, key, value));

        /// <summary>
        /// Replaces provided <paramref name="oldValue"/> with <paramref name="newValue"/> inside 
        /// a bucket under provided <paramref name="key"/> within the context of the current cluster.
        /// </summary>
        public LocalORMultiDictionary<TKey, TVal> ReplaceItem(TKey key, TVal oldValue, TVal newValue) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.ReplaceItem(_currentNode, key, oldValue, newValue));

        /// <summary>
        /// Removes a bucket from underlying ORMultiDictionary in the context of the current cluster node, given a <paramref name="key"/>.
        /// </summary>
        public LocalORMultiDictionary<TKey, TVal> Remove(TKey key) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.Remove(_currentNode, key));

        public ISurrogate ToSurrogate(ActorSystem system) => new Surrogate(_crdt);
        public IEnumerator<KeyValuePair<TKey, IImmutableSet<TVal>>> GetEnumerator() => _crdt.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Merges data from provided <see cref="ORMultiDictionary{TKey,TValue}"/> into current CRDT,
        /// creating new immutable instance in a result.
        /// </summary>
        public LocalORMultiDictionary<TKey, TVal> Merge(ORMultiDictionary<TKey, TVal> dictionary) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.Merge(dictionary));

        public static implicit operator ORMultiDictionary<TKey, TVal>(LocalORMultiDictionary<TKey, TVal> set) => set._crdt;
    }
}