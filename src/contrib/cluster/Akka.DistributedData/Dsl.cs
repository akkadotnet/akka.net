//-----------------------------------------------------------------------
// <copyright file="Dsl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DistributedData
{
    /// <summary>
    /// A helper class used to simplify creation of messages send through 
    /// the <see cref="Replicator"/>.
    /// </summary>
    public static class Dsl
    {
        /// <summary>
        /// Gets a <see cref="IReadConsistency"/> setup, which will acknowledge success of 
        /// a <see cref="Get"/> operation immediately as soon, as result will be 
        /// confirmed by the local replica only.
        /// </summary>
        public static ReadLocal ReadLocal => Akka.DistributedData.ReadLocal.Instance;

        /// <summary>
        /// Gets a <see cref="IWriteConsistency"/> setup, which will acknowledge success of an
        /// <see cref="Update{T}(IKey{T}, T, IWriteConsistency)">Update</see> or <see cref="Delete"/> operation immediately as soon, as 
        /// result will be confirmed by the local replica only.
        /// </summary>
        public static WriteLocal WriteLocal => Akka.DistributedData.WriteLocal.Instance;

        /// <summary>
        /// Constructs a message that, when send to <see cref="DistributedData.Replicator"/>,
        /// will be replied with <see cref="GetKeysIdsResult"/> message having a list of keys 
        /// for all replicated data structures stored on the current node.
        /// </summary>
        public static GetKeyIds GetKeyIds => GetKeyIds.Instance;

        /// <summary>
        /// Constructs a message that, when send to <see cref="DistributedData.Replicator"/>,
        /// will be replied with <see cref="ReplicaCount"/> message having a number of all 
        /// replicas known to the current node.
        /// </summary>
        public static GetReplicaCount GetReplicaCount => GetReplicaCount.Instance;

        /// <summary>
        /// Constructs a message that, when send to <see cref="DistributedData.Replicator"/>,
        /// will perform an update of a structure stored under provided <paramref name="key"/>.
        /// If a <paramref name="value"/> will differ from the one already stored, a merge
        /// operation will be automatically performed in order for those values to converge.
        /// 
        /// An optional <paramref name="consistency"/> level may be provided to set up certain 
        /// constraints on retrieved <see cref="IUpdateResponse"/> replied from replicator as 
        /// a result. If no <paramref name="consistency"/> will be provided, a <see cref="WriteLocal"/>
        /// will be used as a default.
        /// </summary>
        /// <typeparam name="T">Replicated data type.</typeparam>
        /// <param name="key">Key under which a replicated <paramref name="value"/> will be stored.</param>
        /// <param name="value">Replicated data structure to be updated.</param>
        /// <param name="consistency">Consistency determining how/when response will be emitted.</param>
        /// <returns></returns>
        public static Update Update<T>(IKey<T> key, T value, IWriteConsistency consistency = null) 
            where T : IReplicatedData<T> =>
            new Update(key, value, consistency ?? WriteLocal, old => old.Merge(value));

        /// <summary>
        /// Constructs a message that, when send to <see cref="DistributedData.Replicator"/>,
        /// will perform an update of a structure stored under provided <paramref name="key"/>.
        /// Update operation is described by <paramref name="modify"/> function, that will take
        /// a previous value as an input parameter.
        /// 
        /// An <paramref name="consistency"/> level may be provided to set up certain constraints 
        /// on retrieved <see cref="IUpdateResponse"/> replied from replicator as a result. 
        /// </summary>
        /// <typeparam name="T">Replicated data type.</typeparam>
        /// <param name="key">Key under which a value should be updated.</param>
        /// <param name="consistency">Consistency determining how/when response will be emitted.</param>
        /// <param name="modify">An updating function.</param>
        /// <returns>TBD</returns>
        public static Update Update<T>(IKey<T> key, IWriteConsistency consistency, Func<T, T> modify) where T : IReplicatedData =>
            new Update(key, consistency, data => modify((T)data));

        /// <summary>
        /// Constructs a message that, when send to <see cref="DistributedData.Replicator"/>,
        /// will perform an update of a structure stored under provided <paramref name="key"/>.
        /// Update operation is described by <paramref name="modify"/> function, that will take
        /// a previous value as an input parameter. If no value was found under the <paramref name="key"/>,
        /// an <paramref name="initial"/> value will be used instead.
        /// 
        /// An <paramref name="consistency"/> level may be provided to set up certain constraints 
        /// on retrieved <see cref="IUpdateResponse"/> replied from replicator as a result. 
        /// </summary>
        /// <typeparam name="T">Replicated data type.</typeparam>
        /// <param name="key">Key under which a value should be updated.</param>
        /// <param name="initial">Initial value used, when no value has been stored under the provided <paramref name="key"/> so far.</param>
        /// <param name="consistency">Consistency determining how/when response will be emitted.</param>
        /// <param name="modify">An updating function.</param>
        /// <returns>TBD</returns>
        public static Update Update<T>(IKey<T> key, T initial, IWriteConsistency consistency, Func<T, T> modify) where T : IReplicatedData =>
            new Update(key, initial, consistency, data => modify((T)data));

        /// <summary>
        /// Constructs a message that, when send to <see cref="DistributedData.Replicator"/>,
        /// will perform an update of a structure stored under provided <paramref name="key"/>.
        /// Update operation is described by <paramref name="modify"/> function, that will take
        /// a previous value as an input parameter. If no value was found under the <paramref name="key"/>,
        /// an <paramref name="initial"/> value will be used instead.
        /// 
        /// An <paramref name="consistency"/> level may be provided to set up certain constraints 
        /// on retrieved <see cref="IUpdateResponse"/> replied from replicator as a result. 
        /// </summary>
        /// <typeparam name="T">Replicated data type.</typeparam>
        /// <param name="key">Key under which a value should be updated.</param>
        /// <param name="initial">Initial value used, when no value has been stored under the provided <paramref name="key"/> so far.</param>
        /// <param name="consistency">Consistency determining how/when response will be emitted.</param>
        /// <param name="request">
        /// An object added to both generated <see cref="Akka.DistributedData.Update"/> request and 
        /// <see cref="IUpdateResponse"/>. Can be used i.e. as correlation id.
        /// </param>
        /// <param name="modify">An updating function.</param>
        /// <returns>TBD</returns>
        public static Update Update<T>(IKey<T> key, T initial, IWriteConsistency consistency, object request, Func<T, T> modify) where T : IReplicatedData =>
            new Update(key, initial, consistency, data => modify((T)data), request);

        /// <summary>
        /// Constructs a message that, when send to <see cref="DistributedData.Replicator"/>,
        /// will perform a retrieve of a data structure stored under provided <paramref name="key"/>,
        /// and reply with <see cref="IGetResponse"/> message.
        /// 
        /// An optional <paramref name="consistency"/> level may be supplied in order to apply
        /// certain constraints on the produced response.
        /// </summary>
        /// <typeparam name="T">Replicated data type.</typeparam>
        /// <param name="key">Key, for which a value should be retrieved.</param>
        /// <param name="consistency">A consistency level determining when/how response will be retrieved.</param>
        /// <param name="request">
        /// An object added to both generated <see cref="Akka.DistributedData.Get"/> request and 
        /// <see cref="IGetResponse"/>. Can be used i.e. as correlation id.
        /// </param>
        /// <returns>TBD</returns>
        public static Get Get<T>(IKey<T> key, IReadConsistency consistency = null, object request = null) where T : IReplicatedData =>
            new Get(key, consistency ?? ReadLocal, request);

        /// <summary>
        /// Constructs a message that, when send to <see cref="DistributedData.Replicator"/>,
        /// will perform a delete of a structure stored under provided <paramref name="key"/>,
        /// and reply with <see cref="IDeleteResponse"/> message.
        /// 
        /// A delete is irrecoverable - you cannot reinsert a value under the key that has been 
        /// deleted. If you try, a <see cref="DataDeleted"/> response will be send back.
        /// 
        /// A deletion doesn't clear all of the memory used by the store. Some portion of it is
        /// used to keep track of deleted records across the nodes.
        /// </summary>
        /// <typeparam name="T">Replicated data type.</typeparam>
        /// <param name="key">Key, for which a value should be retrieved.</param>
        /// <param name="consistency">A consistency level determining when/how response will be retrieved.</param>
        /// <param name="request">
        /// An object added to both generated <see cref="Akka.DistributedData.Get"/> request and 
        /// <see cref="IGetResponse"/>. Can be used i.e. as correlation id.
        /// </param>
        /// <returns>TBD</returns>
        public static Delete Delete<T>(IKey<T> key, IWriteConsistency consistency, object request = null) where T: IReplicatedData =>
            new Delete(key, consistency, request);

        /// <summary>
        /// Constructs a message that, when send to <see cref="DistributedData.Replicator"/>,
        /// will perform a subscription of provided <paramref name="subscriber"/> actor to any
        /// changes performed on the provided <paramref name="key"/>.
        /// 
        /// All changes will be send in form of a <see cref="Changed"/> message to the 
        /// <paramref name="subscriber"/>.
        /// </summary>
        /// <typeparam name="T">Replicated data type.</typeparam>
        /// <param name="key">Key used to subscribe an actor to all changes occurring in correlated data structure.</param>
        /// <param name="subscriber">Actor subscribing to changes under provided <paramref name="key"/>.</param>
        /// <returns>TBD</returns>
        public static Subscribe Subscribe<T>(IKey<T> key, IActorRef subscriber) where T : IReplicatedData =>
            new Subscribe(key, subscriber);

        /// <summary>
        /// Constructs a message that, when send to <see cref="DistributedData.Replicator"/>,
        /// will perform a unsubscription of provided <paramref name="subscriber"/> actor from
        /// list of provided <paramref name="key"/> subscriptions.
        /// </summary>
        /// <typeparam name="T">Replicated data type.</typeparam>
        /// <param name="key">Key, to which a <paramref name="subscriber"/> has been subscribed previously.</param>
        /// <param name="subscriber">A subscriber for the <paramref name="key"/>ed value changes.</param>
        /// <returns>TBD</returns>
        public static Unsubscribe Unsubscribe<T>(IKey<T> key, IActorRef subscriber) where T : IReplicatedData =>
            new Unsubscribe(key, subscriber);
    }
}
