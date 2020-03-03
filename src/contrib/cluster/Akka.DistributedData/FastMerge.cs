//-----------------------------------------------------------------------
// <copyright file="FastMerge.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public abstract class FastMerge<T> : IReplicatedData<T> where T : FastMerge<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal FastMerge<T> Ancestor = null;

        /// <summary>
        /// INTERNAL API: should be called from "updating" methods
        /// </summary>
        /// <param name="newData">TBD</param>
        /// <returns>TBD</returns>
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
        /// <param name="newData">TBD</param>
        /// <returns>TBD</returns>
        [InternalApi]
        protected bool IsAncestorOf(T newData) => ReferenceEquals(newData.Ancestor, this);

        /// <summary>
        /// INTERNAL API: should be called from merge 
        /// </summary>
        /// <returns>TBD</returns>
        [InternalApi]
        protected T ClearAncestor()
        {
            Ancestor = null;
            return (T)this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public abstract T Merge(T other);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public IReplicatedData Merge(IReplicatedData other) => Merge((T)other);
    }
}
