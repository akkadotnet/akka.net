//-----------------------------------------------------------------------
// <copyright file="MemorySnapshotStoreShared.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Cluster.Sharding.Tests
{
    public class MemorySnapshotStoreShared : SnapshotStoreProxy
    {
        public override TimeSpan Timeout { get; }

        public MemorySnapshotStoreShared()
        {
            Timeout = Context.System.Settings.Config.GetTimeSpan("akka.persistence.memory-snapshot-store-shared.timeout", null);
        }

        public static void SetStore(IActorRef store, ActorSystem system)
        {
            Persistence.Persistence.Instance.Get(system).SnapshotStoreFor(null).Tell(new SetStore(store));
        }
    }
}
