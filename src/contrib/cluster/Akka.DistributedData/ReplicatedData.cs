//-----------------------------------------------------------------------
// <copyright file="ReplicatedData.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Cluster;

namespace Akka.DistributedData
{
    public interface IReplicatedData
    {
        IReplicatedData Merge(IReplicatedData other);
    }

    /// <summary>
    /// Interface for implementing a state based convergent
    /// replicated data type (CvRDT).
    /// 
    /// ReplicatedData types must be serializable with an Akka
    /// Serializer. It is highly recommended to implement a serializer with
    /// Protobuf or similar. The built in data types are marked with
    /// <see cref="IReplicatedDataSerialization"/> and serialized with
    /// <see cref="ReplicatedDataSerializer"/>.
    /// 
    /// Serialization of the data types are used in remote messages and also
    /// for creating message digests (SHA-1) to detect changes. Therefore it is
    /// important that the serialization produce the same bytes for the same content.
    /// For example sets and maps should be sorted deterministically in the serialization.
    /// 
    /// ReplicatedData types should be immutable, i.e. "modifying" methods should return
    /// a new instance.
    /// </summary>
    public interface IReplicatedData<T> : IReplicatedData where T : IReplicatedData
    {
        /// <summary>
        /// Monotonic merge method.
        /// </summary>
        T Merge(T other);
    }

    /// <summary>
    /// <see cref="IReplicatedData"/> that has support for pruning of data
    /// belonging to a specific node may implement this interface.
    /// When a node is removed from the cluster these methods will be
    /// used by the <see cref="Replicator"/> to collapse data from the removed node
    /// into some other node in the cluster.
    /// </summary>
    public interface IRemovedNodePruning<T> : IReplicatedData<T> where T : IReplicatedData
    {
        /// <summary>
        /// Does it have any state changes from a specific node,
        /// which has been removed from the cluster.
        /// </summary>
        bool NeedPruningFrom(UniqueAddress removedNode);

        /// <summary>
        /// When the <paramref name="removedNode"/> node has been removed from the cluster the state
        /// changes from that node will be pruned by collapsing the data entries
        /// to another node.
        /// </summary>
        T Prune(UniqueAddress removedNode, UniqueAddress collapseInto);

        /// <summary>
        /// Remove data entries from a node that has been removed from the cluster
        /// and already been pruned.
        /// </summary>
        T PruningCleanup(UniqueAddress removedNode);
    }

    public interface IRemovedNodePruning : IRemovedNodePruning<IReplicatedData>
    {

    }
}
