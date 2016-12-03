//-----------------------------------------------------------------------
// <copyright file="PNCounter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster;
using System;
using System.Collections.Immutable;
using System.Numerics;
using Akka.Actor;
using Akka.Util;

namespace Akka.DistributedData
{
    /// <summary>
    /// Implements a 'Increment/Decrement Counter' CRDT, also called a 'PN-Counter'.
    /// 
    /// It is described in the paper
    /// <a href="http://hal.upmc.fr/file/index/docid/555588/filename/techreport.pdf">A comprehensive study of Convergent and Commutative Replicated Data Types</a>.
    /// 
    /// PN-Counters allow the counter to be incremented by tracking the
    /// increments (P) separate from the decrements (N). Both P and N are represented
    /// as two internal [[GCounter]]s. Merge is handled by merging the internal P and N
    /// counters. The value of the counter is the value of the P _counter minus
    /// the value of the N counter.
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    [Serializable]
    public sealed class PNCounter : IReplicatedData<PNCounter>, IRemovedNodePruning<PNCounter>, IReplicatedDataSerialization, IEquatable<PNCounter>
    {
        public static readonly PNCounter Empty = new PNCounter();

        public BigInteger Value => Increments.Value - Decrements.Value;

        public GCounter Increments { get; }

        public GCounter Decrements { get; }

        public PNCounter() : this(GCounter.Empty, GCounter.Empty) { }

        public PNCounter(GCounter increments, GCounter decrements)
        {
            Increments = increments;
            Decrements = decrements;
        }
        
        /// <summary>
        /// Increment the counter with the delta specified.
        /// If the delta is negative then it will decrement instead of increment.
        /// </summary>
        public PNCounter Increment(UniqueAddress address, long delta = 1) => 
            Increment(address, new BigInteger(delta));

        /// <summary>
        /// Decrement the counter with the delta specified.
        /// If the delta is negative then it will increment instead of decrement.
        /// </summary>
        public PNCounter Decrement(UniqueAddress address, long delta = 1) => 
            Decrement(address, new BigInteger(delta));

        /// <summary>
        /// Increment the counter with the delta specified.
        /// If the delta is negative then it will decrement instead of increment.
        /// </summary>
        public PNCounter Increment(UniqueAddress address, BigInteger delta) => Change(address, delta);

        /// <summary>
        /// Decrement the counter with the delta specified.
        /// If the delta is negative then it will increment instead of decrement.
        /// </summary>
        public PNCounter Decrement(UniqueAddress address, BigInteger delta) => Change(address, -delta);

        private PNCounter Change(UniqueAddress key, BigInteger delta)
        {
            if (delta > 0) return new PNCounter(Increments.Increment(key, delta), Decrements);
            if (delta < 0) return new PNCounter(Increments, Decrements.Increment(key, -delta));

            return this;
        }

        public PNCounter Merge(PNCounter other) => 
            new PNCounter(Increments.Merge(other.Increments), Decrements.Merge(other.Decrements));

        public bool NeedPruningFrom(Cluster.UniqueAddress removedNode) => 
            Increments.NeedPruningFrom(removedNode) || Decrements.NeedPruningFrom(removedNode);

        public PNCounter Prune(Cluster.UniqueAddress removedNode, Cluster.UniqueAddress collapseInto) => 
            new PNCounter(Increments.Prune(removedNode, collapseInto), Decrements.Prune(removedNode, collapseInto));

        public PNCounter PruningCleanup(Cluster.UniqueAddress removedNode) => 
            new PNCounter(Increments.PruningCleanup(removedNode), Decrements.PruningCleanup(removedNode));

        public bool Equals(PNCounter other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return other.Increments.Equals(Increments) && other.Decrements.Equals(Decrements);
        }

        public override string ToString() => $"PNCounter({Value})";

        public override bool Equals(object obj) => obj is PNCounter && Equals((PNCounter) obj);

        public override int GetHashCode() => Increments.GetHashCode() ^ Decrements.GetHashCode();

        public IReplicatedData Merge(IReplicatedData other) => Merge((PNCounter) other);
    }

    public sealed class PNCounterKey : Key<PNCounter>
    {
        public PNCounterKey(string id)
            : base(id)
        { }
    }
}
