//-----------------------------------------------------------------------
// <copyright file="LocalLWWRegister.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData.Local
{
    /// <summary>
    /// A wrapper around <see cref="LWWRegister{T}"/> that will work in the context of the current cluster node.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public struct LocalLWWRegister<T> : ISurrogated
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Surrogate : ISurrogate
        {
            private readonly LWWRegister<T> register;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="register">TBD</param>
            public Surrogate(LWWRegister<T> register)
            {
                this.register = register;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="system">TBD</param>
            /// <returns>TBD</returns>
            public ISurrogated FromSurrogate(ActorSystem system) =>
                new LocalLWWRegister<T>(Cluster.Cluster.Get(system), register);
        }

        private readonly UniqueAddress _currentNode;
        private readonly LWWRegister<T> _crdt;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="currentNode">TBD</param>
        /// <param name="crdt">TBD</param>
        internal LocalLWWRegister(UniqueAddress currentNode, LWWRegister<T> crdt) : this()
        {
            _currentNode = currentNode;
            _crdt = crdt;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cluster">TBD</param>
        /// <param name="crdt">TBD</param>
        public LocalLWWRegister(Cluster.Cluster cluster, LWWRegister<T> crdt) : this(cluster.SelfUniqueAddress, crdt)
        {
        }

        /// <summary>
        /// Returns a timestamp used to determine precedence in current register updates.
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
        /// Change the value of the underlying register.
        /// </summary>
        /// <param name="value">TBD</param>
        /// <param name="clock">TBD</param>
        /// <returns>TBD</returns>
        public LocalLWWRegister<T> WithValue(T value, Clock<T> clock = null) =>
            new LocalLWWRegister<T>(_currentNode, _crdt.WithValue(_currentNode, value, clock));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public ISurrogate ToSurrogate(ActorSystem system) => new Surrogate(_crdt);

        /// <summary>
        /// Merges data from provided <see cref="LWWRegister{T}"/> into current CRDT,
        /// creating new immutable instance in a result.
        /// </summary>
        /// <param name="register">TBD</param>
        /// <returns>TBD</returns>
        public LocalLWWRegister<T> Merge(LWWRegister<T> register) =>
            new LocalLWWRegister<T>(_currentNode, _crdt.Merge(register));

        /// <summary>
        /// Performs an implicit conversion from <see cref="Akka.DistributedData.Local.LocalLWWRegister{T}" /> to <see cref="Akka.DistributedData.LWWRegister{T}" />.
        /// </summary>
        /// <param name="set">The set to convert</param>
        /// <returns>The result of the conversion</returns>
        public static implicit operator LWWRegister<T>(LocalLWWRegister<T> set) => set._crdt;
    }
}