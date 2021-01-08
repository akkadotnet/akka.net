//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreLoadBehaviorSetter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using System.Threading.Tasks;
    using Actor;

    /// <summary>
    ///     Setter strategy for <see cref="TestSnapshotStore"/> which will set load interceptor.
    /// </summary>
    internal class SnapshotStoreLoadBehaviorSetter : ISnapshotStoreBehaviorSetter
    {
        internal SnapshotStoreLoadBehaviorSetter(IActorRef snapshots)
        {
            this._snapshots = snapshots;
        }

        private readonly IActorRef _snapshots;

        public Task SetInterceptorAsync(ISnapshotStoreInterceptor interceptor)
            => _snapshots.Ask<TestSnapshotStore.Ack>(
                new TestSnapshotStore.UseLoadInterceptor(interceptor),
                TimeSpan.FromSeconds(3)
            );
    }
}
