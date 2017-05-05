//-----------------------------------------------------------------------
// <copyright file="LocalORMultiDictionary.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData.Local
{
    /// <summary>
    /// A wrapper around <see cref="ORMultiValueDictionary{TKey,TValue}"/> that works in context of the current cluster.
    /// </summary>
    /// <typeparam name="TKey">TBD</typeparam>
    /// <typeparam name="TVal">TBD</typeparam>
    public struct LocalORMultiDictionary<TKey, TVal> : ISurrogated, IEnumerable<KeyValuePair<TKey, IImmutableSet<TVal>>>
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Surrogate : ISurrogate
        {
            private readonly ORMultiValueDictionary<TKey, TVal> _dictionary;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="dictionary">TBD</param>
            public Surrogate(ORMultiValueDictionary<TKey, TVal> dictionary)
            {
                _dictionary = dictionary;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="system">TBD</param>
            /// <returns>TBD</returns>
            public ISurrogated FromSurrogate(ActorSystem system) =>
                new LocalORMultiDictionary<TKey, TVal>(Cluster.Cluster.Get(system), _dictionary);
        }

        private readonly UniqueAddress _currentNode;
        private readonly ORMultiValueDictionary<TKey, TVal> _crdt;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="currentNode">TBD</param>
        /// <param name="crdt">TBD</param>
        internal LocalORMultiDictionary(UniqueAddress currentNode, ORMultiValueDictionary<TKey, TVal> crdt) : this()
        {
            _currentNode = currentNode;
            _crdt = crdt;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cluster">TBD</param>
        /// <param name="crdt">TBD</param>
        public LocalORMultiDictionary(Cluster.Cluster cluster, ORMultiValueDictionary<TKey, TVal> crdt) : this(cluster.SelfUniqueAddress, crdt)
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
        /// Returns number of elements inside the underlying ORMultiDictionary.
        /// </summary>
        public int Count => _crdt.Count;

        /// <summary>
        /// Determines if underlying ORMultiDictionary is empty.
        /// </summary>
        public bool IsEmpty => _crdt.IsEmpty;

        /// <summary>
        /// Gets or sets provided key-value of the underlying ORMultiDictionary within scope of the current cluster node.
        /// </summary>
        /// <param name="key">TBD</param>
        public IImmutableSet<TVal> this[TKey key] => _crdt[key];

        /// <summary>
        /// Gets value determining, if underlying ORMultiDictionary contains specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <returns>TBD</returns>
        public bool ContainsKey(TKey key) => _crdt.ContainsKey(key);

        /// <summary>
        /// Tries to retrieve element stored under provided <paramref name="key"/> in the underlying ORMultiDictionary,
        /// returning true if such value existed.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetValue(TKey key, out IImmutableSet<TVal> value) => _crdt.TryGetValue(key, out value);

        /// <summary>
        /// Stored provided <paramref name="value"/> in entry with given <paramref name="key"/> inside the
        /// underlying ORMultiDictionary in scope of the current node, and returning new local dictionary in result.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public LocalORMultiDictionary<TKey, TVal> SetItems(TKey key, IImmutableSet<TVal> value) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.SetItems(_currentNode, key, value));

        /// <summary>
        /// Adds provided <paramref name="value"/> into a bucket under provided <paramref name="key"/>
        /// within the context of the current cluster.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public LocalORMultiDictionary<TKey, TVal> AddItem(TKey key, TVal value) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.AddItem(_currentNode, key, value));

        /// <summary>
        /// Removes provided <paramref name="value"/> from a bucket under provided <paramref name="key"/>
        /// within the context of the current cluster.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public LocalORMultiDictionary<TKey, TVal> RemoveItem(TKey key, TVal value) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.RemoveItem(_currentNode, key, value));

        /// <summary>
        /// Replaces provided <paramref name="oldValue"/> with <paramref name="newValue"/> inside 
        /// a bucket under provided <paramref name="key"/> within the context of the current cluster.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <param name="oldValue">TBD</param>
        /// <param name="newValue">TBD</param>
        /// <returns>TBD</returns>
        public LocalORMultiDictionary<TKey, TVal> ReplaceItem(TKey key, TVal oldValue, TVal newValue) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.ReplaceItem(_currentNode, key, oldValue, newValue));

        /// <summary>
        /// Removes a bucket from underlying ORMultiDictionary in the context of the current cluster node, given a <paramref name="key"/>.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <returns>TBD</returns>
        public LocalORMultiDictionary<TKey, TVal> Remove(TKey key) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.Remove(_currentNode, key));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public ISurrogate ToSurrogate(ActorSystem system) => new Surrogate(_crdt);
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IEnumerator<KeyValuePair<TKey, IImmutableSet<TVal>>> GetEnumerator() => _crdt.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Merges data from provided <see cref="ORMultiValueDictionary{TKey,TValue}"/> into current CRDT,
        /// creating new immutable instance in a result.
        /// </summary>
        /// <param name="dictionary">TBD</param>
        /// <returns>TBD</returns>
        public LocalORMultiDictionary<TKey, TVal> Merge(ORMultiValueDictionary<TKey, TVal> dictionary) =>
            new LocalORMultiDictionary<TKey, TVal>(_currentNode, _crdt.Merge(dictionary));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="set">TBD</param>
        /// <returns>TBD</returns>
        public static implicit operator ORMultiValueDictionary<TKey, TVal>(LocalORMultiDictionary<TKey, TVal> set) => set._crdt;
    }
}