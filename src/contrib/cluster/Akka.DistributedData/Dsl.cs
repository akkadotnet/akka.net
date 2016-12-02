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
    public static class Dsl
    {
        public static Replicator.GetKeyIds GetKeyIds => Replicator.GetKeyIds.Instance;

        public static Replicator.GetReplicaCount GetReplicaCount => Replicator.GetReplicaCount.Instance;

        public static Replicator.Update Update<T>(IKey<T> key, T initial, IWriteConsistency consistency, Func<T, T> modify) where T : IReplicatedData =>
            new Replicator.Update(key, initial, consistency, data => modify((T)data));

        public static Replicator.Update Update<T>(IKey<T> key, T initial, IWriteConsistency consistency, object request, Func<T, T> modify) where T : IReplicatedData =>
            new Replicator.Update(key, initial, consistency, data => modify((T)data), request);

        public static Replicator.Get Get<T>(IKey<T> key, IReadConsistency consistency) where T : IReplicatedData =>
            new Replicator.Get(key, consistency);

        public static Replicator.Get Get<T>(IKey<T> key, IReadConsistency consistency, object request) where T : IReplicatedData =>
            new Replicator.Get(key, consistency, request);

        public static Replicator.Delete Delete<T>(IKey<T> key, IWriteConsistency consistency) where T: IReplicatedData =>
            new Replicator.Delete(key, consistency);

        public static Replicator.Subscribe Subscribe<T>(IKey<T> key, IActorRef subscriberRef) where T : IReplicatedData =>
            new Replicator.Subscribe(key, subscriberRef);

        public static Replicator.Unsubscribe Unsubscribe<T>(IKey<T> key, IActorRef subscriberRef) where T : IReplicatedData =>
            new Replicator.Unsubscribe(key, subscriberRef);
    }
}