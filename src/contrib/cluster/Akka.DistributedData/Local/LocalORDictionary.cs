using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData.Local
{
    /// <summary>
    /// A wrapper around <see cref="ORDictionary{TKey,TValue}"/> that works in context of the current cluster.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    /// <typeparam name="TVal"></typeparam>
    public struct LocalORDictionary<TKey, TVal> : ISurrogated, IEnumerable<KeyValuePair<TKey, TVal>> where TVal : IReplicatedData
    {
        internal sealed class Surrogate : ISurrogate
        {
            private readonly ORDictionary<TKey, TVal> _dictionary;

            public Surrogate(ORDictionary<TKey, TVal> dictionary)
            {
                _dictionary = dictionary;
            }

            public ISurrogated FromSurrogate(ActorSystem system) =>
                new LocalORDictionary<TKey, TVal>(Cluster.Cluster.Get(system), _dictionary);
        }

        private readonly UniqueAddress _currentNode;
        private readonly ORDictionary<TKey, TVal> _crdt;

        internal LocalORDictionary(UniqueAddress currentNode, ORDictionary<TKey, TVal> crdt) : this()
        {
            _currentNode = currentNode;
            _crdt = crdt;
        }

        public LocalORDictionary(Cluster.Cluster cluster, ORDictionary<TKey, TVal> crdt) : this(cluster.SelfUniqueAddress, crdt)
        {
        }

        /// <summary>
        /// Returns collection of the elements inside the current set.
        /// </summary>
        public IImmutableDictionary<TKey, TVal> Entries => _crdt.Entries;

        /// <summary>
        /// Returns number of elements inside the current set.
        /// </summary>
        public int Count => _crdt.Count;

        /// <summary>
        /// Determines if underlying ORDictionary is empty.
        /// </summary>
        public bool IsEmpty => _crdt.IsEmpty;

        /// <summary>
        /// Gets or sets provided key-valu of the underlying ORDicationary within scope of the current cluster node.
        /// </summary>
        public TVal this[TKey key] => _crdt[key];

        /// <summary>
        /// Gets value determining, if underlying ORDictionary contains specified <paramref name="key"/>.
        /// </summary>
        public bool ContainsKey(TKey key) => _crdt.ContainsKey(key);

        /// <summary>
        /// Tries to retrieve element stored under provided <paramref name="key"/> in the underlying ORDictionary,
        /// returning true if such value existed.
        /// </summary>
        public bool TryGetValue(TKey key, out TVal value) => _crdt.TryGetValue(key, out value);

        /// <summary>
        /// Stored provided <paramref name="value"/> in entry with given <paramref name="key"/> inside the
        /// underlying ORDictionary in scope of the current node, and returning new local dictionary in result.
        /// </summary>
        public LocalORDictionary<TKey, TVal> SetItem(TKey key, TVal value) =>
            new LocalORDictionary<TKey, TVal>(_currentNode, _crdt.SetItem(_currentNode, key, value));

        /// <summary>
        /// Adds or updated a value in entry with given <paramref name="key"/> using <paramref name="modify"/> function
        /// if other value existed there previously, within a constext of the current cluster node.
        /// </summary>
        public LocalORDictionary<TKey, TVal> AddOrUpdate(TKey key, TVal value, Func<TVal, TVal> modify) =>
            new LocalORDictionary<TKey, TVal>(_currentNode, _crdt.AddOrUpdate(_currentNode, key, value, modify));

        /// <summary>
        /// Removes an entry from underlying ORDictionary in the context of the current cluster node, given a <paramref name="key"/>.
        /// </summary>
        public LocalORDictionary<TKey, TVal> Remove(TKey key) =>
            new LocalORDictionary<TKey, TVal>(_currentNode, _crdt.Remove(_currentNode, key));

        public ISurrogate ToSurrogate(ActorSystem system) => new Surrogate(_crdt);
        public IEnumerator<KeyValuePair<TKey, TVal>> GetEnumerator() => _crdt.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Merges data from provided <see cref="ORDictionary{TKey,TValue}"/> into current CRDT,
        /// creating new immutable instance in a result.
        /// </summary>
        public LocalORDictionary<TKey, TVal> Merge(ORDictionary<TKey, TVal> dictionary) =>
            new LocalORDictionary<TKey, TVal>(_currentNode, _crdt.Merge(dictionary));

        public static implicit operator ORDictionary<TKey, TVal>(LocalORDictionary<TKey, TVal> set) => set._crdt;
    }
}