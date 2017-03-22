//-----------------------------------------------------------------------
// <copyright file="LocalLWWDictionary.cs" company="Akka.NET Project">
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
    /// A wrapper around <see cref="LWWDictionary{TKey,TValue}"/> that works in the context of the current cluster node.
    /// </summary>
    /// <typeparam name="TKey">TBD</typeparam>
    /// <typeparam name="TVal">TBD</typeparam>
    public struct LocalLWWDictionary<TKey, TVal> : ISurrogated, IEnumerable<KeyValuePair<TKey, TVal>>
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Surrogate : ISurrogate
        {
            private readonly LWWDictionary<TKey, TVal> _dictionary;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="dictionary">TBD</param>
            public Surrogate(LWWDictionary<TKey, TVal> dictionary)
            {
                _dictionary = dictionary;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="system">TBD</param>
            /// <returns>TBD</returns>
            public ISurrogated FromSurrogate(ActorSystem system) =>
                new LocalLWWDictionary<TKey, TVal>(Cluster.Cluster.Get(system), _dictionary);
        }

        private readonly UniqueAddress _currentNode;
        private readonly LWWDictionary<TKey, TVal> _crdt;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="currentNode">TBD</param>
        /// <param name="crdt">TBD</param>
        internal LocalLWWDictionary(UniqueAddress currentNode, LWWDictionary<TKey, TVal> crdt) : this()
        {
            _currentNode = currentNode;
            _crdt = crdt;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cluster">TBD</param>
        /// <param name="crdt">TBD</param>
        public LocalLWWDictionary(Cluster.Cluster cluster, LWWDictionary<TKey, TVal> crdt) : this(cluster.SelfUniqueAddress, crdt)
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
        /// Determines if underlying LWWDictionary is empty.
        /// </summary>
        public bool IsEmpty => _crdt.IsEmpty;

        /// <summary>
        /// Gets or sets provided key-valu of the underlying ORDicationary within scope of the current cluster node.
        /// </summary>
        /// <param name="key">TBD</param>
        public TVal this[TKey key] => _crdt[key];

        /// <summary>
        /// Gets value determining, if underlying LWWDictionary contains specified <paramref name="key"/>.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <returns>TBD</returns>
        public bool ContainsKey(TKey key) => _crdt.ContainsKey(key);

        /// <summary>
        /// Tries to retrieve element stored under provided <paramref name="key"/> in the underlying LWWDictionary,
        /// returning true if such value existed.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public bool TryGetValue(TKey key, out TVal value) => _crdt.TryGetValue(key, out value);

        /// <summary>
        /// Stored provided <paramref name="value"/> in entry with given <paramref name="key"/> inside the
        /// underlying LWWDictionary in scope of the current node, and returning new local dictionary in result.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public LocalLWWDictionary<TKey, TVal> SetItem(TKey key, TVal value) =>
            new LocalLWWDictionary<TKey, TVal>(_currentNode, _crdt.SetItem(_currentNode, key, value));

        /// <summary>
        /// Removes an entry from underlying LWWDictionary in the context of the current cluster node, given a <paramref name="key"/>.
        /// </summary>
        /// <param name="key">TBD</param>
        /// <returns>TBD</returns>
        public LocalLWWDictionary<TKey, TVal> Remove(TKey key) =>
            new LocalLWWDictionary<TKey, TVal>(_currentNode, _crdt.Remove(_currentNode, key));

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
        public IEnumerator<KeyValuePair<TKey, TVal>> GetEnumerator() => _crdt.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Merges data from provided <see cref="LWWDictionary{TKey,TValue}"/> into current CRDT,
        /// creating new immutable instance in a result.
        /// </summary>
        /// <param name="dictionary">TBD</param>
        /// <returns>TBD</returns>
        public LocalLWWDictionary<TKey, TVal> Merge(LWWDictionary<TKey, TVal> dictionary) => 
            new LocalLWWDictionary<TKey, TVal>(_currentNode, _crdt.Merge(dictionary));

        /// <summary>
        /// Performs an implicit conversion from <see cref="Akka.DistributedData.Local.LocalLWWDictionary{TKey, TVal}" /> to <see cref="Akka.DistributedData.LWWDictionary{TKey, TVal}" />.
        /// </summary>
        /// <param name="set">The set to convert</param>
        /// <returns>The result of the conversion</returns>
        public static implicit operator LWWDictionary<TKey, TVal>(LocalLWWDictionary<TKey, TVal> set) => set._crdt;
    }
}