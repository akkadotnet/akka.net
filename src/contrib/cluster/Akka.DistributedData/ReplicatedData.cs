//-----------------------------------------------------------------------
// <copyright file="ReplicatedData.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Cluster;
using Akka.DistributedData.Serialization;

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

    public interface IRemovedNodePruning : IReplicatedData
    {
        /// <summary>
        /// The nodes that have changed the state for this data
        /// and would need pruning when such node is no longer part
        /// of the cluster.
        /// </summary>
        ImmutableHashSet<UniqueAddress> ModifiedByNodes { get; }

        /// <summary>
        /// Does it have any state changes from a specific node,
        /// which has been removed from the cluster.
        /// </summary>
        bool NeedPruningFrom(UniqueAddress removedNode);
        IReplicatedData PruningCleanup(UniqueAddress removedNode);
        IReplicatedData Prune(UniqueAddress removedNode, UniqueAddress collapseInto);
    }

    /// <summary>
    /// <see cref="IReplicatedData"/> that has support for pruning of data
    /// belonging to a specific node may implement this interface.
    /// When a node is removed from the cluster these methods will be
    /// used by the <see cref="Replicator"/> to collapse data from the removed node
    /// into some other node in the cluster.
    /// </summary>
    public interface IRemovedNodePruning<T> : IRemovedNodePruning, IReplicatedData<T> where T : IReplicatedData
    {

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

    /// <summary>
    /// <see cref="IReplicatedData"/> with additional support for delta-CRDT replication.
    /// delta-CRDT is a way to reduce the need for sending the full state
    /// for updates. For example adding element 'c' and 'd' to set {'a', 'b'} would
    /// result in sending the delta {'c', 'd'} and merge that with the state on the
    /// receiving side, resulting in set {'a', 'b', 'c', 'd'}.
    /// 
    /// Learn more about this in the paper
    /// <a href="paper http://arxiv.org/abs/1603.01529">Delta State Replicated Data Types</a>.
    /// </summary>
    public interface IDeltaReplicatedData : IReplicatedData
    {
        IReplicatedDelta Delta { get; }
        IReplicatedData MergeDelta(IReplicatedDelta delta);
        IReplicatedData ResetDelta();
    }

    /// <summary>
    /// <see cref="IReplicatedData{T}"/> with additional support for delta-CRDT replication.
    /// delta-CRDT is a way to reduce the need for sending the full state
    /// for updates. For example adding element 'c' and 'd' to set {'a', 'b'} would
    /// result in sending the delta {'c', 'd'} and merge that with the state on the
    /// receiving side, resulting in set {'a', 'b', 'c', 'd'}.
    /// 
    /// Learn more about this in the paper
    /// <a href="paper http://arxiv.org/abs/1603.01529">Delta State Replicated Data Types</a>.
    /// </summary>
    /// <typeparam name="TDelta">
    /// The type of the delta. To be specified by subclass. It may be the same type as `T` or 
    /// a different type if needed. For example <see cref="GSet{T}"/> uses the same type and 
    /// <see cref="ORSet{T}"/> uses different types.
    /// </typeparam>
    /// <typeparam name="T">Replicated data type</typeparam>
    public interface IDeltaReplicatedData<T, TDelta> : IDeltaReplicatedData, IReplicatedData<T>
        where T : IReplicatedData
        where TDelta : IReplicatedDelta
    {
        /// <summary>
        /// The accumulated delta of mutator operations since previous
        /// <see cref="ResetDelta"/>. When the <see cref="Replicator"/> invokes the `modify` function
        /// of the <see cref="Update"/> message and the user code is invoking one or more mutator
        /// operations the data is collecting the delta of the operations and makes
        /// it available for the <see cref="Replicator"/> with the <see cref="Delta"/> accessor. The
        /// `modify` function shall still return the full state in the same way as
        /// <see cref="IReplicatedData{T}"/> without support for deltas.
        /// </summary>
        TDelta Delta { get; }

        /// <summary>
        /// When delta is merged into the full state this method is used.
        /// When the type <typeparamref name="TDelta"/> of the delta is of the same type as the full 
        /// state <typeparamref name="T"/> this method can be implemented by delegating to 
        /// <see cref="IReplicatedData{T}.Merge(T)"/>.
        /// </summary>
        T MergeDelta(TDelta delta);

        /// <summary>
        /// Reset collection of deltas from mutator operations. When the <see cref="Replicator"/>
        /// invokes the `modify` function of the <see cref="Update"/> message the delta is always
        /// "reset" and when the user code is invoking one or more mutator operations the
        /// data is collecting the delta of the operations and makes it available for
        /// the <see cref="Replicator"/> with the <see cref="Delta"/> accessor. When the
        /// <see cref="Replicator"/> has grabbed the <see cref="Delta"/> it will invoke this method 
        /// to get a clean data instance without the delta.
        /// </summary>
        T ResetDelta();
    }

    /// <summary>
    /// The delta must implement this type.
    /// </summary>
    public interface IReplicatedDelta : IReplicatedData
    {
        /// <summary>
        /// The empty full state. This is used when a delta is received
        /// and no existing full state exists on the receiving side. Then
        /// the delta is merged into the <see cref="Zero"/> to create the initial full state.
        /// </summary>
        IDeltaReplicatedData Zero { get; }
    }

    /// <summary>
    /// Marker that specifies that the deltas must be applied in causal order.
    /// There is some overhead of managing the causal delivery so it should only
    /// be used for types that need it.
    /// 
    /// Note that if the full state type `T` is different from the delta type `D`
    /// it is the delta `D` that should be marked with this.
    /// </summary>
    public interface IRequireCausualDeliveryOfDeltas : IReplicatedDelta { }

    /// <summary>
    /// Some complex deltas grow in size for each update and above a configured
    /// threshold such deltas are discarded and sent as full state instead. This
    /// interface should be implemented by such deltas to define its size.
    /// </summary>
    public interface IReplicatedDeltaSize
    {
        /// <summary>
        /// Returns delta size of target delta operation.
        /// </summary>
        int DeltaSize { get; }
    }
}
