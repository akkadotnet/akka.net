//-----------------------------------------------------------------------
// <copyright file="FastMerge.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Annotations;

namespace Akka.DistributedData
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Optimization for add/remove followed by merge and merge should just fast forward to
    /// the new instance.
    /// 
    /// It's like a cache between calls of the same thread, you can think of it as a thread local.
    /// The Replicator actor invokes the user's modify function, which returns a new ReplicatedData instance,
    /// with the ancestor field set (see for example the add method in ORSet). Then (in same thread) the
    /// Replication calls merge, which makes use of the ancestor field to perform quick merge
    /// (see for example merge method in ORSet).
    /// 
    /// It's not thread safe if the modifying function and merge are called from different threads,
    /// i.e. if used outside the Replicator infrastructure, but the worst thing that can happen is that
    /// a full merge is performed instead of the fast forward merge.
    /// </summary>
    /// <typeparam name="T">The concrete replicated-data type that supports fast-forward merge.</typeparam>
    [InternalApi]
    public abstract class FastMerge<T> : IReplicatedData<T> where T : FastMerge<T>
    {
        /// <summary>
        /// The previously linked instance used to recognize an update followed by its merge.
        /// </summary>
        internal FastMerge<T> Ancestor = null;

        /// <summary>
        /// INTERNAL API: should be called from "updating" methods
        /// </summary>
        /// <param name="newData">The updated instance to associate with this instance.</param>
        /// <returns><paramref name="newData"/> with its ancestor link assigned.</returns>
        [InternalApi]
        protected T AssignAncestor(T newData)
        {
            newData.Ancestor = Ancestor ?? this;
            Ancestor = null;
            return newData;
        }

        /// <summary>
        /// INTERNAL API: should be used from merge
        /// </summary>
        /// <param name="newData">The candidate descendant instance.</param>
        /// <returns><see langword="true"/> when <paramref name="newData"/> records this instance as its ancestor.</returns>
        [InternalApi]
        protected bool IsAncestorOf(T newData) => ReferenceEquals(newData.Ancestor, this);

        /// <summary>
        /// INTERNAL API: should be called from merge 
        /// </summary>
        /// <returns>This instance after clearing its ancestor link.</returns>
        [InternalApi]
        protected T ClearAncestor()
        {
            Ancestor = null;
            return (T)this;
        }

        /// <summary>
        /// Merges this replicated-data value with another value of the same concrete type.
        /// </summary>
        /// <param name="other">The value to merge.</param>
        /// <returns>The merged value.</returns>
        public abstract T Merge(T other);

        /// <summary>
        /// Merges this replicated-data value with another replicated-data value.
        /// </summary>
        /// <param name="other">A value of the same concrete type as this instance.</param>
        /// <returns>The merged replicated-data value.</returns>
        public IReplicatedData Merge(IReplicatedData other) => Merge((T)other);
    }
}
