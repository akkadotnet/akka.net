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
        public static LocalGCounter GCounter(this Cluster.Cluster cluster) => 
            new LocalGCounter(cluster, Akka.DistributedData.GCounter.Empty);

        /// <summary>
        /// Creates an instance of GCounter scoped to a current cluster.
        /// </summary>
        public static LocalGCounter GCounter(this Cluster.Cluster cluster, GCounter counter) =>
            new LocalGCounter(cluster, counter);

        /// <summary>
        /// Creates a new instance of PNCounter, that works within the scope of the current cluster.
        /// </summary>
        public static LocalPNCounter PNCounter(this Cluster.Cluster cluster) =>
            new LocalPNCounter(cluster, Akka.DistributedData.PNCounter.Empty);

        /// <summary>
        /// Creates an instance of PNCounter scoped to a current cluster.
        /// </summary>
        public static LocalPNCounter PNCounter(this Cluster.Cluster cluster, PNCounter counter) =>
            new LocalPNCounter(cluster, counter);

        /// <summary>
        /// Creates a new instance of <see cref="LocalORSet{T}"/>, that works within the scope of the current cluster.
        /// </summary>
        public static LocalORSet<T> ORSet<T>(this Cluster.Cluster cluster) =>
            new LocalORSet<T>(cluster, Akka.DistributedData.ORSet<T>.Empty);

        /// <summary>
        /// Creates an instance of an ORSet scoped to a current cluster.
        /// </summary>
        public static LocalORSet<T> ORSet<T>(this Cluster.Cluster cluster, ORSet<T> orset) =>
            new LocalORSet<T>(cluster, orset);

        /// <summary>
        /// Creates a new instance of an ORDictionary scoped to a current cluster.
        /// </summary>
        public static LocalORDictionary<TKey, TVal> ORDictionary<TKey, TVal>(this Cluster.Cluster cluster) where TVal : IReplicatedData =>
            new LocalORDictionary<TKey, TVal>(cluster, Akka.DistributedData.ORDictionary<TKey, TVal>.Empty);

        /// <summary>
        /// Creates an instance of an ORDictionary scoped to a current cluster.
        /// </summary>
        public static LocalORDictionary<TKey, TVal> ORDictionary<TKey, TVal>(this Cluster.Cluster cluster, ORDictionary<TKey, TVal> dictionary) where TVal : IReplicatedData =>
            new LocalORDictionary<TKey, TVal>(cluster, dictionary);

        /// <summary>
        /// Creates a new instance of an ORMultiDictionary scoped to a current cluster.
        /// </summary>
        public static LocalORMultiDictionary<TKey, TVal> ORMultiDictionary<TKey, TVal>(this Cluster.Cluster cluster)  =>
            new LocalORMultiDictionary<TKey, TVal>(cluster, Akka.DistributedData.ORMultiDictionary<TKey, TVal>.Empty);

        /// <summary>
        /// Creates an instance of an ORMultiDictionary scoped to a current cluster.
        /// </summary>
        public static LocalORMultiDictionary<TKey, TVal> ORMultiDictionary<TKey, TVal>(this Cluster.Cluster cluster, ORMultiDictionary<TKey, TVal> dictionary) =>
            new LocalORMultiDictionary<TKey, TVal>(cluster, dictionary);

        /// <summary>
        /// Creates a new instance of an PNCounterDictionary scoped to a current cluster.
        /// </summary>
        public static LocalPNCounterDictionary<TKey> PNCounterDictionary<TKey>(this Cluster.Cluster cluster) =>
            new LocalPNCounterDictionary<TKey>(cluster, Akka.DistributedData.PNCounterDictionary<TKey>.Empty);

        /// <summary>
        /// Creates an instance of an PNCounterDictionary scoped to a current cluster.
        /// </summary>
        public static LocalPNCounterDictionary<TKey> PNCounterDictionary<TKey>(this Cluster.Cluster cluster, PNCounterDictionary<TKey> dictionary) =>
            new LocalPNCounterDictionary<TKey>(cluster, dictionary);
        
        /// <summary>
        /// Creates an instance of an LWWRegister scoped to a current cluster.
        /// </summary>
        public static LocalLWWRegister<TVal> LWWRegister<TVal>(this Cluster.Cluster cluster, LWWRegister<TVal> register) =>
            new LocalLWWRegister<TVal>(cluster, register);
        /// <summary>
        /// Creates a new instance of an LWWDictionary scoped to a current cluster.
        /// </summary>
        public static LocalLWWDictionary<TKey, TVal> LWWDictionary<TKey, TVal>(this Cluster.Cluster cluster) =>
            new LocalLWWDictionary<TKey, TVal>(cluster, Akka.DistributedData.LWWDictionary<TKey, TVal>.Empty);

        /// <summary>
        /// Creates an instance of an LWWDictionary scoped to a current cluster.
        /// </summary>
        public static LocalLWWDictionary<TKey, TVal> LWWDictionary<TKey, TVal>(this Cluster.Cluster cluster, LWWDictionary<TKey, TVal> dictionary) =>
            new LocalLWWDictionary<TKey, TVal>(cluster, dictionary);

    }
}