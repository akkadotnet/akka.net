﻿//-----------------------------------------------------------------------
// <copyright file="GCounter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using Akka.Actor;
using Akka.Util;

namespace Akka.DistributedData
{
    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public sealed class GCounterKey : Key<GCounter>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        public GCounterKey(string id) : base(id) { }
    }

    /// <summary>
    /// Implements a 'Growing Counter' CRDT, also called a 'G-Counter'.
    /// 
    /// It is described in the paper
    /// <a href="http://hal.upmc.fr/file/index/docid/555588/filename/techreport.pdf">A comprehensive study of Convergent and Commutative Replicated Data Types</a>.
    /// 
    /// A G-Counter is a increment-only counter (inspired by vector clocks) in
    /// which only increment and merge are possible. Incrementing the counter
    /// adds 1 to the count for the current node. Divergent histories are
    /// resolved by taking the maximum count for each node (like a vector
    /// clock merge). The value of the counter is the sum of all node counts.
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    [Serializable]
    public sealed class GCounter : FastMerge<GCounter>, IRemovedNodePruning<GCounter>, IEquatable<GCounter>, IComparable<GCounter>, IComparable, IReplicatedDataSerialization
    {
        private static readonly BigInteger Zero = new BigInteger(0);

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableDictionary<UniqueAddress, BigInteger> State { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public static GCounter Empty => new GCounter();

        /// <summary>
        /// Current total value of the counter.
        /// </summary>
        public BigInteger Value { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public GCounter() : this(ImmutableDictionary<UniqueAddress, BigInteger>.Empty) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="state">TBD</param>
        public GCounter(IImmutableDictionary<UniqueAddress, BigInteger> state)
        {
            State = state;
            Value = State.Aggregate(Zero, (v, acc) => v + acc.Value);
        }
        /// <summary>
        /// Increment the counter by 1.
        /// </summary>
        public GCounter Increment(Cluster.Cluster node) => Increment(node.SelfUniqueAddress);

        /// <summary>
        /// Increment the counter by 1.
        /// </summary>
        public GCounter Increment(UniqueAddress node) => Increment(node, new BigInteger(1));

        /// <summary>
        /// Increment the counter with the delta specified. The delta must be zero or positive.
        /// </summary>
        public GCounter Increment(Cluster.Cluster node, ulong delta) => Increment(node.SelfUniqueAddress, delta);

        /// <summary>
        /// Increment the counter with the delta specified. The delta must be zero or positive.
        /// </summary>
        public GCounter Increment(UniqueAddress node, ulong delta) => Increment(node, new BigInteger(delta));

        /// <summary>
        /// Increment the counter with the delta specified. The delta must be zero or positive.
        /// </summary>
        public GCounter Increment(Cluster.Cluster node, BigInteger delta) => Increment(node.SelfUniqueAddress, delta);

        /// <summary>
        /// Increment the counter with the delta specified. The delta must be zero or positive.
        /// </summary>
        /// <param name="node">TBD</param>
        /// <param name="delta">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public GCounter Increment(UniqueAddress node, BigInteger delta)
        {
            if (delta < 0) throw new ArgumentException("Can't decrement a GCounter");
            if (delta == 0) return this;

            BigInteger v;
            if (State.TryGetValue(node, out v))
            {
                var total = v + delta;
                return AssignAncestor(new GCounter(State.SetItem(node, total)));
            }
            else return AssignAncestor(new GCounter(State.SetItem(node, delta)));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public override GCounter Merge(GCounter other)
        {
            if (ReferenceEquals(this, other) || other.IsAncestorOf(this)) return ClearAncestor();
            else if (IsAncestorOf(other)) return other.ClearAncestor();
            else
            {
                var merged = other.State;
                foreach (var kvp in State)
                {
                    var otherValue = merged.GetValueOrDefault(kvp.Key, Zero);
                    if (kvp.Value > otherValue)
                    {
                        merged = merged.SetItem(kvp.Key, kvp.Value);
                    }
                }
                ClearAncestor();
                return new GCounter(merged);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedNode">TBD</param>
        /// <returns>TBD</returns>
        public bool NeedPruningFrom(UniqueAddress removedNode) => State.ContainsKey(removedNode);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedNode">TBD</param>
        /// <param name="collapseInto">TBD</param>
        /// <returns>TBD</returns>
        public GCounter Prune(UniqueAddress removedNode, UniqueAddress collapseInto)
        {
            BigInteger prunedNodeValue;
            return State.TryGetValue(removedNode, out prunedNodeValue) 
                ? new GCounter(State.Remove(removedNode)).Increment(collapseInto, prunedNodeValue) 
                : this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedNode">TBD</param>
        /// <returns>TBD</returns>
        public GCounter PruningCleanup(UniqueAddress removedNode) => new GCounter(State.Remove(removedNode));

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            return State.GetHashCode();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public int CompareTo(object obj)
        {
            if (obj is GCounter) return CompareTo((GCounter) obj);
            return -1;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(GCounter other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Equals(Value, other.Value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj) => obj is GCounter && Equals((GCounter)obj);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public int CompareTo(GCounter other)
        {
            if (ReferenceEquals(other, null)) return 1;
            return Value.CompareTo(other.Value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"GCounter({Value})";

        /// <summary>
        /// Performs an implicit conversion from <see cref="Akka.DistributedData.GCounter" /> to <see cref="System.Numerics.BigInteger" />.
        /// </summary>
        /// <param name="counter">The counter to convert</param>
        /// <returns>The result of the conversion</returns>
        public static implicit operator BigInteger(GCounter counter) => counter.Value;
    }
}
