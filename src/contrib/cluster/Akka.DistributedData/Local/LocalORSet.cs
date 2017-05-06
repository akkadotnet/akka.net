//-----------------------------------------------------------------------
// <copyright file="LocalORSet.cs" company="Akka.NET Project">
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
    /// A wrapper around <see cref="ORSet{T}"/> instance, that binds it's operations to a current cluster node.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public struct LocalORSet<T> : ISurrogated, IEnumerable<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Surrogate : ISurrogate
        {
            private readonly ORSet<T> _set;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="set">TBD</param>
            public Surrogate(ORSet<T> set)
            {
                _set = set;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="system">TBD</param>
            /// <returns>TBD</returns>
            public ISurrogated FromSurrogate(ActorSystem system) =>
                new LocalORSet<T>(Cluster.Cluster.Get(system).SelfUniqueAddress, _set);
        }

        private readonly UniqueAddress _currentNode;
        private readonly ORSet<T> _crdt;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="currentNode">TBD</param>
        /// <param name="crdt">TBD</param>
        internal LocalORSet(UniqueAddress currentNode, ORSet<T> crdt) : this()
        {
            _currentNode = currentNode;
            _crdt = crdt;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cluster">TBD</param>
        /// <param name="crdt">TBD</param>
        public LocalORSet(Cluster.Cluster cluster, ORSet<T> crdt) : this(cluster.SelfUniqueAddress, crdt)
        {
        }

        /// <summary>
        /// Returns collection of the elements inside the current set.
        /// </summary>
        public IImmutableSet<T> Elements => _crdt.Elements;

        /// <summary>
        /// Returns number of elements inside the current set.
        /// </summary>
        public int Count => _crdt.Count;

        /// <summary>
        /// Clears underlying ORSet in scope of the current cluster node.
        /// </summary>
        /// <returns>TBD</returns>
        public LocalORSet<T> Clear() => new LocalORSet<T>(_currentNode, _crdt.Clear(_currentNode));

        /// <summary>
        /// Checks if target <paramref name="element"/> exists within underlying ORSet.
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public bool Contains(T element) => _crdt.Contains(element);

        /// <summary>
        /// Adds an <paramref name="element"/> to the underlying ORSet in scope of a current cluster node.
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public LocalORSet<T> Add(T element) => new LocalORSet<T>(_currentNode, _crdt.Add(_currentNode, element));

        /// <summary>
        /// Removes an <paramref name="element"/> from the underlying ORSet in scope of a current cluster node.
        /// </summary>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public LocalORSet<T> Remove(T element) => new LocalORSet<T>(_currentNode, _crdt.Remove(_currentNode, element));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public ISurrogate ToSurrogate(ActorSystem system) => new Surrogate(_crdt);

        /// <inheritdoc/>
        public IEnumerator<T> GetEnumerator() => _crdt.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Merges data from provided <see cref="ORSet{T}"/> into current CRDT,
        /// creating new immutable instance in a result.
        /// </summary>
        /// <param name="set">TBD</param>
        /// <returns>TBD</returns>
        public LocalORSet<T> Merge(ORSet<T> set) => new LocalORSet<T>(_currentNode, _crdt.Merge(set));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="set">TBD</param>
        /// <returns>TBD</returns>
        public static implicit operator ORSet<T>(LocalORSet<T> set) => set._crdt;
    }
}