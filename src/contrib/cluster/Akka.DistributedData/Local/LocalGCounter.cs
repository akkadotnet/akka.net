//-----------------------------------------------------------------------
// <copyright file="LocalGCounter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Numerics;
using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData.Local
{
    /// <summary>
    /// A wrapper around <see cref="GCounter"/> instance, that binds it's operations to a current cluster node.
    /// </summary>
    public struct LocalGCounter : ISurrogated
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Surrogate : ISurrogate
        {
            private readonly GCounter _counter;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="counter">TBD</param>
            public Surrogate(GCounter counter)
            {
                this._counter = counter;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="system">TBD</param>
            /// <returns>TBD</returns>
            public ISurrogated FromSurrogate(ActorSystem system) =>
                new LocalGCounter(Cluster.Cluster.Get(system).SelfUniqueAddress, _counter);
        }

        private readonly UniqueAddress _currentNode;
        private readonly GCounter _crdt;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="currentNode">TBD</param>
        /// <param name="crdt">TBD</param>
        internal LocalGCounter(UniqueAddress currentNode, GCounter crdt) : this()
        {
            _currentNode = currentNode;
            _crdt = crdt;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cluster">TBD</param>
        /// <param name="counter"></param>
        public LocalGCounter(Cluster.Cluster cluster, GCounter counter) : this(cluster.SelfUniqueAddress, counter)
        {
        }

        /// <summary>
        /// Returns a value of the underlying GCounter.
        /// </summary>
        public BigInteger Value => _crdt.Value;

        /// <summary>
        /// Merges data from provided <see cref="GCounter"/> into current CRDT,
        /// creating new immutable instance in a result.
        /// </summary>
        /// <param name="counter">TBD</param>
        /// <returns>TBD</returns>
        public LocalGCounter Merge(GCounter counter) => new LocalGCounter(_currentNode, _crdt.Merge(counter));

        /// <summary>
        /// Increments current GCounter value by 1 in current cluster node context.
        /// </summary>
        /// <param name="counter">TBD</param>
        /// <returns>TBD</returns>
        public static LocalGCounter operator ++(LocalGCounter counter)
        {
            var node = counter._currentNode;
            return new LocalGCounter(node, counter._crdt.Increment(node));
        }

        /// <summary>
        /// Increments current GCounter value by provided <paramref name="delta"/> in current cluster node context.
        /// </summary>
        /// <param name="counter">TBD</param>
        /// <param name="delta">TBD</param>
        /// <returns>TBD</returns>
        public static LocalGCounter operator +(LocalGCounter counter, ulong delta)
        {
            var node = counter._currentNode;
            return new LocalGCounter(node, counter._crdt.Increment(node, delta));
        }

        /// <summary>
        /// Increments current GCounter value by provided <paramref name="delta"/> in current cluster node context.
        /// </summary>
        /// <param name="counter">TBD</param>
        /// <param name="delta">TBD</param>
        /// <returns>TBD</returns>
        public static LocalGCounter operator +(LocalGCounter counter, BigInteger delta)
        {
            var node = counter._currentNode;
            return new LocalGCounter(node, counter._crdt.Increment(node, delta));
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="Akka.DistributedData.Local.LocalGCounter" /> to <see cref="Akka.DistributedData.GCounter" />.
        /// </summary>
        /// <param name="counter">The counter to convert</param>
        /// <returns>The result of the conversion</returns>
        public static implicit operator GCounter(LocalGCounter counter) => counter._crdt;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public ISurrogate ToSurrogate(ActorSystem system) => new Surrogate(_crdt);
    }
}