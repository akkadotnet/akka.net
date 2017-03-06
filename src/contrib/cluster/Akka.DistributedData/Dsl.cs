//-----------------------------------------------------------------------
// <copyright file="Dsl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DistributedData
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class Dsl
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static GetKeyIds GetKeyIds => GetKeyIds.Instance;

        /// <summary>
        /// TBD
        /// </summary>
        public static GetReplicaCount GetReplicaCount => GetReplicaCount.Instance;

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="key">TBD</param>
        /// <param name="consistency">TBD</param>
        /// <param name="modify">TBD</param>
        /// <returns>TBD</returns>
        public static Update Update<T>(IKey<T> key, IWriteConsistency consistency, Func<T, T> modify) where T : IReplicatedData =>
            new Update(key, consistency, data => modify((T)data));

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="key">TBD</param>
        /// <param name="initial">TBD</param>
        /// <param name="consistency">TBD</param>
        /// <param name="modify">TBD</param>
        /// <returns>TBD</returns>
        public static Update Update<T>(IKey<T> key, T initial, IWriteConsistency consistency, Func<T, T> modify) where T : IReplicatedData =>
            new Update(key, initial, consistency, data => modify((T)data));

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="key">TBD</param>
        /// <param name="initial">TBD</param>
        /// <param name="consistency">TBD</param>
        /// <param name="request">TBD</param>
        /// <param name="modify">TBD</param>
        /// <returns>TBD</returns>
        public static Update Update<T>(IKey<T> key, T initial, IWriteConsistency consistency, object request, Func<T, T> modify) where T : IReplicatedData =>
            new Update(key, initial, consistency, data => modify((T)data), request);
        
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="key">TBD</param>
        /// <param name="consistency">TBD</param>
        /// <param name="request">TBD</param>
        /// <returns>TBD</returns>
        public static Get Get<T>(IKey<T> key, IReadConsistency consistency, object request = null) where T : IReplicatedData =>
            new Get(key, consistency, request);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="key">TBD</param>
        /// <param name="consistency">TBD</param>
        /// <returns>TBD</returns>
        public static Delete Delete<T>(IKey<T> key, IWriteConsistency consistency, object request = null) where T: IReplicatedData =>
            new Delete(key, consistency, request);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="key">TBD</param>
        /// <param name="subscriberRef">TBD</param>
        /// <returns>TBD</returns>
        public static Subscribe Subscribe<T>(IKey<T> key, IActorRef subscriberRef) where T : IReplicatedData =>
            new Subscribe(key, subscriberRef);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="key">TBD</param>
        /// <param name="subscriberRef">TBD</param>
        /// <returns>TBD</returns>
        public static Unsubscribe Unsubscribe<T>(IKey<T> key, IActorRef subscriberRef) where T : IReplicatedData =>
            new Unsubscribe(key, subscriberRef);
    }
}