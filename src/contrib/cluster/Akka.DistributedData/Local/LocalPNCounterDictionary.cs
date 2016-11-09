using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData.Local
{

    /// <summary>
    /// Wrapper around <see cref="PNCounterDictionary{TKey}"/> that provides 
    /// execution context of the current cluster node.
    /// </summary>
    /// <typeparam name="TKey"></typeparam>
    public struct LocalPNCounterDictionary<TKey> : ISurrogated, IEnumerable<KeyValuePair<TKey, BigInteger>>
    {
        internal sealed class Surrogate : ISurrogate
        {
            private readonly PNCounterDictionary<TKey> _dictionary;

            public Surrogate(PNCounterDictionary<TKey> dictionary)
            {
                _dictionary = dictionary;
            }

            public ISurrogated FromSurrogate(ActorSystem system) =>
                new LocalPNCounterDictionary<TKey>(Cluster.Cluster.Get(system), _dictionary);
        }

        private readonly UniqueAddress _currentNode;
        private readonly PNCounterDictionary<TKey> _crdt;

        internal LocalPNCounterDictionary(UniqueAddress currentNode, PNCounterDictionary<TKey> crdt) : this()
        {
            _currentNode = currentNode;
            _crdt = crdt;
        }

        public LocalPNCounterDictionary(Cluster.Cluster cluster, PNCounterDictionary<TKey> crdt) : this(cluster.SelfUniqueAddress, crdt)
        {
        }

        /// <summary>
        /// Returns collection of the elements inside the underlying PNCounterDictionary.
        /// </summary>
        public IImmutableDictionary<TKey, BigInteger> Entries => _crdt.Entries;

        /// <summary>
        /// Returns all keys stored within underlying PNCounterDictionary.
        /// </summary>
        public IEnumerable<TKey> Keys => _crdt.Keys;

        /// <summary>
        /// Returns all values stored in all buckets within underlying PNCounterDictionary.
        /// </summary>
        public IEnumerable<BigInteger> Values => _crdt.Values;

        /// <summary>
        /// Returns number of elements inside the unterlying PNCounterDictionary.
        /// </summary>
        public int Count => _crdt.Count;

        /// <summary>
        /// Determines if underlying PNCounterDictionary is empty.
        /// </summary>
        public bool IsEmpty => _crdt.IsEmpty;

        /// <summary>
        /// Gets or sets provided key-valu of the underlying PNCounterDictionary within scope of the current cluster node.
        /// </summary>
        public BigInteger this[TKey key] => _crdt[key];

        /// <summary>
        /// Gets value determining, if underlying PNCounterDictionary contains specified <paramref name="key"/>.
        /// </summary>
        public bool ContainsKey(TKey key) => _crdt.ContainsKey(key);

        /// <summary>
        /// Tries to retrieve element stored under provided <paramref name="key"/> in the underlying PNCounterDictionary,
        /// returning true if such value existed.
        /// </summary>
        public bool TryGetValue(TKey key, out BigInteger value) => _crdt.TryGetValue(key, out value);

        /// <summary>
        /// Increment the counter with the delta specified.
        /// If the delta is negative then it will decrement instead of increment.
        /// </summary>
        public LocalPNCounterDictionary<TKey> Increment(TKey key, long delta = 1L) =>
            new LocalPNCounterDictionary<TKey>(_currentNode, _crdt.Increment(_currentNode, key, delta));

        /// <summary>
        /// Decrement the counter with the delta specified.
        /// If the delta is negative then it will increment instead of decrement.
        /// </summary>
        public LocalPNCounterDictionary<TKey> Decrement(TKey key, long delta = 1L) =>
            new LocalPNCounterDictionary<TKey>(_currentNode, _crdt.Decrement(_currentNode, key, delta));

        /// <summary>
        /// Removes an entry from the map.
        /// Note that if there is a conflicting update on another node the entry will
        /// not be removed after merge.
        /// </summary>
        public LocalPNCounterDictionary<TKey> Remove(TKey key) =>
            new LocalPNCounterDictionary<TKey>(_currentNode, _crdt.Remove(_currentNode, key));

        public ISurrogate ToSurrogate(ActorSystem system) => new Surrogate(_crdt);

        public IEnumerator<KeyValuePair<TKey, BigInteger>> GetEnumerator() => _crdt.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Merges data from provided <see cref="PNCounterDictionary{TKey}"/> into current CRDT,
        /// creating new immutable instance in a result.
        /// </summary>
        public LocalPNCounterDictionary<TKey> Merge(PNCounterDictionary<TKey> dictionary) =>
            new LocalPNCounterDictionary<TKey>(_currentNode, _crdt.Merge(dictionary));

        public static implicit operator PNCounterDictionary<TKey>(LocalPNCounterDictionary<TKey> set) => set._crdt;
    }
}