using System.Numerics;
using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData.Local
{

    /// <summary>
    /// A wrapper around <see cref="PNCounter"/> instance, that binds it's operations to a current cluster node.
    /// </summary>
    public struct LocalPNCounter : ISurrogated
    {
        internal sealed class Surrogate : ISurrogate
        {
            private readonly PNCounter _counter;

            public Surrogate(PNCounter counter)
            {
                _counter = counter;
            }

            public ISurrogated FromSurrogate(ActorSystem system) =>
                new LocalPNCounter(Cluster.Cluster.Get(system).SelfUniqueAddress, _counter);
        }

        private readonly UniqueAddress _currentNode;
        private readonly PNCounter _crdt;

        internal LocalPNCounter(UniqueAddress currentNode, PNCounter crdt) : this()
        {
            _currentNode = currentNode;
            _crdt = crdt;
        }

        public LocalPNCounter(Cluster.Cluster cluster, PNCounter counter) : this(cluster.SelfUniqueAddress, counter)
        {
        }

        /// <summary>
        /// Returns value of the underlying PNCounter.
        /// </summary>
        public BigInteger Value => _crdt.Value;

        /// <summary>
        /// Increments value of the underlying PNCounter by 1 in current cluster node context.
        /// </summary>
        public static LocalPNCounter operator ++(LocalPNCounter counter)
        {
            var node = counter._currentNode;
            return new LocalPNCounter(node, counter._crdt.Increment(node));
        }

        /// <summary>
        /// Decrements value of the underlying PNCounter by 1 in current cluster node context.
        /// </summary>
        public static LocalPNCounter operator --(LocalPNCounter counter)
        {
            var node = counter._currentNode;
            return new LocalPNCounter(node, counter._crdt.Decrement(node));
        }

        /// <summary>
        /// Increments value of the underlying PNCounter by provided <paramref name="delta"/> in current cluster node context.
        /// </summary>
        public static LocalPNCounter operator +(LocalPNCounter counter, ulong delta)
        {
            var node = counter._currentNode;
            return new LocalPNCounter(node, counter._crdt.Increment(node, delta));
        }

        /// <summary>
        /// Increments value of the underlying PNCounter by provided <paramref name="delta"/> in current cluster node context.
        /// </summary>
        public static LocalPNCounter operator +(LocalPNCounter counter, BigInteger delta)
        {
            var node = counter._currentNode;
            return new LocalPNCounter(node, counter._crdt.Increment(node, delta));
        }

        /// <summary>
        /// Decrements value of the underlying PNCounter by provided <paramref name="delta"/> in current cluster node context.
        /// </summary>
        public static LocalPNCounter operator -(LocalPNCounter counter, ulong delta)
        {
            var node = counter._currentNode;
            return new LocalPNCounter(node, counter._crdt.Decrement(node, delta));
        }

        /// <summary>
        /// Decrements value of the underlying PNCounter by provided <paramref name="delta"/> in current cluster node context.
        /// </summary>
        public static LocalPNCounter operator -(LocalPNCounter counter, BigInteger delta)
        {
            var node = counter._currentNode;
            return new LocalPNCounter(node, counter._crdt.Decrement(node, delta));
        }

        public static implicit operator PNCounter(LocalPNCounter counter) => counter._crdt;


        /// <summary>
        /// Merges data from provided <see cref="PNCounter"/> into current CRDT,
        /// creating new immutable instance in a result.
        /// </summary>
        public LocalPNCounter Merge(PNCounter counter) => new LocalPNCounter(_currentNode, _crdt.Merge(counter));

        public ISurrogate ToSurrogate(ActorSystem system) => new Surrogate(_crdt);
    }
}