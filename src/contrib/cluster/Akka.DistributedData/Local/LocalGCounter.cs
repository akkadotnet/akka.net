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
        internal sealed class Surrogate : ISurrogate
        {
            private readonly GCounter _counter;

            public Surrogate(GCounter counter)
            {
                this._counter = counter;
            }

            public ISurrogated FromSurrogate(ActorSystem system) =>
                new LocalGCounter(Cluster.Cluster.Get(system).SelfUniqueAddress, _counter);
        }

        private readonly UniqueAddress _currentNode;
        private readonly GCounter _crdt;

        internal LocalGCounter(UniqueAddress currentNode, GCounter crdt) : this()
        {
            _currentNode = currentNode;
            _crdt = crdt;
        }

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
        public LocalGCounter Merge(GCounter counter) => new LocalGCounter(_currentNode, _crdt.Merge(counter));

        /// <summary>
        /// Increments current GCounter value by 1 in current cluster node context.
        /// </summary>
        public static LocalGCounter operator ++(LocalGCounter counter)
        {
            var node = counter._currentNode;
            return new LocalGCounter(node, counter._crdt.Increment(node));
        }

        /// <summary>
        /// Increments current GCounter value by provided <paramref name="delta"/> in current cluster node context.
        /// </summary>
        public static LocalGCounter operator +(LocalGCounter counter, ulong delta)
        {
            var node = counter._currentNode;
            return new LocalGCounter(node, counter._crdt.Increment(node, delta));
        }

        /// <summary>
        /// Increments current GCounter value by provided <paramref name="delta"/> in current cluster node context.
        /// </summary>
        public static LocalGCounter operator +(LocalGCounter counter, BigInteger delta)
        {
            var node = counter._currentNode;
            return new LocalGCounter(node, counter._crdt.Increment(node, delta));
        }

        public static implicit operator GCounter(LocalGCounter counter) => counter._crdt;
        public ISurrogate ToSurrogate(ActorSystem system) => new Surrogate(_crdt);
    }
}