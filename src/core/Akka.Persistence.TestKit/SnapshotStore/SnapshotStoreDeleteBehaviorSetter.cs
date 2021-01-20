﻿//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreDeleteBehaviorSetter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;
    using Actor;

    /// <summary>
    ///     Setter strategy for <see cref="TestSnapshotStore"/> which will set delete interceptor.
    /// </summary>
    internal class SnapshotStoreDeleteBehaviorSetter : ISnapshotStoreBehaviorSetter
    {
        internal SnapshotStoreDeleteBehaviorSetter(IActorRef snapshots)
        {
            this._snapshots = snapshots;
        }

        private readonly IActorRef _snapshots;

        public Task SetInterceptorAsync(ISnapshotStoreInterceptor interceptor)
            => _snapshots.Ask<TestSnapshotStore.Ack>(
                new TestSnapshotStore.UseDeleteInterceptor(interceptor),
                TimeSpan.FromSeconds(3)
            );
    }
}
