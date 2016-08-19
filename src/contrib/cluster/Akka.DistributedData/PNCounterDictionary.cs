//-----------------------------------------------------------------------
// <copyright file="PNCounterDictionary.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using UniqueAddress = Akka.Cluster.UniqueAddress;

namespace Akka.DistributedData
{
    /// <summary>
    /// Map of named counters. Specialized <see cref="ORDictionary{TKey,TValue}"/> 
    /// with <see cref="PNCounter"/> values. 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    public class PNCounterDictionary<TKey> : IReplicatedData<PNCounterDictionary<TKey>>, IRemovedNodePruning<PNCounterDictionary<TKey>>, IReplicatedDataSerialization, IEquatable<PNCounterDictionary<TKey>>
    {
        public static readonly PNCounterDictionary<TKey> Empty = new PNCounterDictionary<TKey>(ORDictionary<TKey, PNCounter>.Empty);

        private readonly ORDictionary<TKey, PNCounter> _underlying;

        public PNCounterDictionary(ORDictionary<TKey, PNCounter> underlying)
        {
            _underlying = underlying;
        }

        public IImmutableDictionary<TKey, BigInteger> Entries => _underlying.Entries
            .Select(kv => new KeyValuePair<TKey, BigInteger>(kv.Key, kv.Value.Value))
            .ToImmutableDictionary();

        public BigInteger this[TKey key] => _underlying[key].Value;

        public bool ContainsKey(TKey key) => _underlying.ContainsKey(key);

        public bool IsEmpty => _underlying.IsEmpty;

        public int Count => _underlying.Count;

        /// <summary>
        /// Increment the counter with the delta specified.
        /// If the delta is negative then it will decrement instead of increment.
        /// </summary>
        public PNCounterDictionary<TKey> Increment(UniqueAddress node, TKey key, long delta = 1L) =>
            new PNCounterDictionary<TKey>(_underlying.AddOrUpdate(node, key, PNCounter.Empty, old => old.Increment(node, delta)));

        /// <summary>
        /// Decrement the counter with the delta specified.
        /// If the delta is negative then it will increment instead of decrement.
        /// </summary>
        public PNCounterDictionary<TKey> Decrement(UniqueAddress node, TKey key, long delta = 1L) =>
            new PNCounterDictionary<TKey>(_underlying.AddOrUpdate(node, key, PNCounter.Empty, old => old.Decrement(node, delta)));

        /// <summary>
        /// Removes an entry from the map.
        /// Note that if there is a conflicting update on another node the entry will
        /// not be removed after merge.
        /// </summary>
        public PNCounterDictionary<TKey> Remove(UniqueAddress node, TKey key) =>
            new PNCounterDictionary<TKey>(_underlying.Remove(node, key));

        public PNCounterDictionary<TKey> Merge(PNCounterDictionary<TKey> other) => 
            new PNCounterDictionary<TKey>(_underlying.Merge(other._underlying));

        public IReplicatedData Merge(IReplicatedData other) => 
            Merge((PNCounterDictionary<TKey>) other);

        public bool NeedPruningFrom(UniqueAddress removedNode) => 
            _underlying.NeedPruningFrom(removedNode);

        public PNCounterDictionary<TKey> Prune(UniqueAddress removedNode, UniqueAddress collapseInto) => 
            new PNCounterDictionary<TKey>(_underlying.Prune(removedNode, collapseInto));

        public PNCounterDictionary<TKey> PruningCleanup(UniqueAddress removedNode) => 
            new PNCounterDictionary<TKey>(_underlying.PruningCleanup(removedNode));

        public bool Equals(PNCounterDictionary<TKey> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(_underlying, other._underlying);
        }

        public override bool Equals(object obj) => 
            obj is PNCounterDictionary<TKey> && Equals((PNCounterDictionary<TKey>) obj);

        public override int GetHashCode() => _underlying.GetHashCode();
    }

    public class PNCounterDictionaryKey<T> : Key<PNCounterDictionary<T>>
    {
        public PNCounterDictionaryKey(string id) : base(id)
        {
        }
    }
}