using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData.Local
{

    /// <summary>
    /// A wrapper around <see cref="LWWRegister{T}"/> that will work in the context of the current cluster node.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public struct LocalLWWRegister<T> : ISurrogated
    {
        internal sealed class Surrogate : ISurrogate
        {
            private readonly LWWRegister<T> register;

            public Surrogate(LWWRegister<T> register)
            {
                this.register = register;
            }

            public ISurrogated FromSurrogate(ActorSystem system) =>
                new LocalLWWRegister<T>(Cluster.Cluster.Get(system), register);
        }

        private readonly UniqueAddress _currentNode;
        private readonly LWWRegister<T> _crdt;

        internal LocalLWWRegister(UniqueAddress currentNode, LWWRegister<T> crdt) : this()
        {
            _currentNode = currentNode;
            _crdt = crdt;
        }

        public LocalLWWRegister(Cluster.Cluster cluster, LWWRegister<T> crdt) : this(cluster.SelfUniqueAddress, crdt)
        {
        }

        /// <summary>
        /// Returns a timestamp used to determine predecende in current register updates.
        /// </summary>
        public long Timestamp => _crdt.Timestamp;

        /// <summary>
        /// Returns value of the current register.
        /// </summary>
        public T Value => _crdt.Value;

        /// <summary>
        /// Returns a unique address of the last cluster node, that updated current register value.
        /// </summary>
        public UniqueAddress UpdatedBy => _crdt.UpdatedBy;

        /// <summary>
        /// Change the value of the undelrying register.
        /// </summary>
        public LocalLWWRegister<T> WithValue(T value, Clock<T> clock = null) =>
            new LocalLWWRegister<T>(_currentNode, _crdt.WithValue(_currentNode, value, clock));

        public ISurrogate ToSurrogate(ActorSystem system) => new Surrogate(_crdt);

        /// <summary>
        /// Merges data from provided <see cref="LWWRegister{T}"/> into current CRDT,
        /// creating new immutable instance in a result.
        /// </summary>
        public LocalLWWRegister<T> Merge(LWWRegister<T> register) =>
            new LocalLWWRegister<T>(_currentNode, _crdt.Merge(register));

        public static implicit operator LWWRegister<T>(LocalLWWRegister<T> set) => set._crdt;
    }
}