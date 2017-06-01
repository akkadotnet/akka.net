//-----------------------------------------------------------------------
// <copyright file="ClusterReplicatedDataExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.DistributedData.Local
{
    /// <summary>
    /// Extensions methods container to ease building CRDTs scoped to local cluster.
    /// </summary>
    public static class ClusterReplicatedDataExtensions
    {
        /// <summary>
        /// Creates a new instance of GCounter, that works within the scope of the current cluster.
        /// </summary>
        /// <param name="cluster">TBD</param>
        /// <returns>TBD</returns>
        public static LocalGCounter GCounter(this Cluster.Cluster cluster) =>
            new LocalGCounter(cluster, Akka.DistributedData.GCounter.Empty);

        /// <summary>
        /// Creates an instance of GCounter scoped to a current cluster.
        /// </summary>
        /// <param name="cluster">TBD</param>
        /// <param name="counter">TBD</param>
        /// <returns>TBD</returns>
        public static LocalGCounter GCounter(this Cluster.Cluster cluster, GCounter counter) =>
            new LocalGCounter(cluster, counter);

        /// <summary>
        /// Creates a new instance of PNCounter, that works within the scope of the current cluster.
        /// </summary>
        /// <param name="cluster">TBD</param>
        /// <returns>TBD</returns>
        public static LocalPNCounter PNCounter(this Cluster.Cluster cluster) =>
            new LocalPNCounter(cluster, Akka.DistributedData.PNCounter.Empty);

        /// <summary>
        /// Creates an instance of PNCounter scoped to a current cluster.
        /// </summary>
        /// <param name="cluster">TBD</param>
        /// <param name="counter">TBD</param>
        /// <returns>TBD</returns>
        public static LocalPNCounter PNCounter(this Cluster.Cluster cluster, PNCounter counter) =>
            new LocalPNCounter(cluster, counter);

        /// <summary>
        /// Creates a new instance of <see cref="LocalORSet{T}"/>, that works within the scope of the current cluster.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="cluster">TBD</param>
        /// <returns>TBD</returns>
        public static LocalORSet<T> ORSet<T>(this Cluster.Cluster cluster) =>
            new LocalORSet<T>(cluster, Akka.DistributedData.ORSet<T>.Empty);

        /// <summary>
        /// Creates an instance of an ORSet scoped to a current cluster.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="cluster">TBD</param>
        /// <param name="orset">TBD</param>
        /// <returns>TBD</returns>
        public static LocalORSet<T> ORSet<T>(this Cluster.Cluster cluster, ORSet<T> orset) =>
            new LocalORSet<T>(cluster, orset);

        /// <summary>
        /// Creates a new instance of an ORDictionary scoped to a current cluster.
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TVal">TBD</typeparam>
        /// <param name="cluster">TBD</param>
        /// <returns>TBD</returns>
        public static LocalORDictionary<TKey, TVal> ORDictionary<TKey, TVal>(this Cluster.Cluster cluster)
            where TVal : IReplicatedData<TVal> =>
            new LocalORDictionary<TKey, TVal>(cluster, Akka.DistributedData.ORDictionary<TKey, TVal>.Empty);

        /// <summary>
        /// Creates an instance of an ORDictionary scoped to a current cluster.
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TVal">TBD</typeparam>
        /// <param name="cluster">TBD</param>
        /// <param name="dictionary">TBD</param>
        /// <returns>TBD</returns>
        public static LocalORDictionary<TKey, TVal> ORDictionary<TKey, TVal>(this Cluster.Cluster cluster, ORDictionary<TKey, TVal> dictionary)
            where TVal : IReplicatedData<TVal> =>
            new LocalORDictionary<TKey, TVal>(cluster, dictionary);

        /// <summary>
        /// Creates a new instance of an ORMultiDictionary scoped to a current cluster.
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TVal">TBD</typeparam>
        /// <param name="cluster">TBD</param>
        /// <returns>TBD</returns>
        public static LocalORMultiDictionary<TKey, TVal> ORMultiDictionary<TKey, TVal>(this Cluster.Cluster cluster) =>
            new LocalORMultiDictionary<TKey, TVal>(cluster, Akka.DistributedData.ORMultiValueDictionary<TKey, TVal>.Empty);

        /// <summary>
        /// Creates an instance of an ORMultiDictionary scoped to a current cluster.
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TVal">TBD</typeparam>
        /// <param name="cluster">TBD</param>
        /// <param name="dictionary">TBD</param>
        /// <returns>TBD</returns>
        public static LocalORMultiDictionary<TKey, TVal> ORMultiDictionary<TKey, TVal>(this Cluster.Cluster cluster, ORMultiValueDictionary<TKey, TVal> dictionary) =>
            new LocalORMultiDictionary<TKey, TVal>(cluster, dictionary);

        /// <summary>
        /// Creates a new instance of an PNCounterDictionary scoped to a current cluster.
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <param name="cluster">TBD</param>
        /// <returns>TBD</returns>
        public static LocalPNCounterDictionary<TKey> PNCounterDictionary<TKey>(this Cluster.Cluster cluster) =>
            new LocalPNCounterDictionary<TKey>(cluster, Akka.DistributedData.PNCounterDictionary<TKey>.Empty);

        /// <summary>
        /// Creates an instance of an PNCounterDictionary scoped to a current cluster.
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <param name="cluster">TBD</param>
        /// <param name="dictionary">TBD</param>
        /// <returns>TBD</returns>
        public static LocalPNCounterDictionary<TKey> PNCounterDictionary<TKey>(this Cluster.Cluster cluster, PNCounterDictionary<TKey> dictionary) =>
            new LocalPNCounterDictionary<TKey>(cluster, dictionary);

        /// <summary>
        /// Creates an instance of an LWWRegister scoped to a current cluster.
        /// </summary>
        /// <typeparam name="TVal">TBD</typeparam>
        /// <param name="cluster">TBD</param>
        /// <param name="register">TBD</param>
        /// <returns>TBD</returns>
        public static LocalLWWRegister<TVal> LWWRegister<TVal>(this Cluster.Cluster cluster, LWWRegister<TVal> register) =>
            new LocalLWWRegister<TVal>(cluster, register);
        /// <summary>
        /// Creates a new instance of an LWWDictionary scoped to a current cluster.
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TVal">TBD</typeparam>
        /// <param name="cluster">TBD</param>
        /// <returns>TBD</returns>
        public static LocalLWWDictionary<TKey, TVal> LWWDictionary<TKey, TVal>(this Cluster.Cluster cluster) =>
            new LocalLWWDictionary<TKey, TVal>(cluster, Akka.DistributedData.LWWDictionary<TKey, TVal>.Empty);

        /// <summary>
        /// Creates an instance of an LWWDictionary scoped to a current cluster.
        /// </summary>
        /// <typeparam name="TKey">TBD</typeparam>
        /// <typeparam name="TVal">TBD</typeparam>
        /// <param name="cluster">TBD</param>
        /// <param name="dictionary">TBD</param>
        /// <returns>TBD</returns>
        public static LocalLWWDictionary<TKey, TVal> LWWDictionary<TKey, TVal>(this Cluster.Cluster cluster, LWWDictionary<TKey, TVal> dictionary) =>
            new LocalLWWDictionary<TKey, TVal>(cluster, dictionary);
    }
}