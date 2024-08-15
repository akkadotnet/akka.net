// -----------------------------------------------------------------------
//  <copyright file="SnapshotStoreDeleteBehaviorSetter.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.TestKit;

/// <summary>
///     Setter strategy for <see cref="TestSnapshotStore" /> which will set delete interceptor.
/// </summary>
internal class SnapshotStoreDeleteBehaviorSetter : ISnapshotStoreBehaviorSetter
{
    private readonly IActorRef _snapshots;

    internal SnapshotStoreDeleteBehaviorSetter(IActorRef snapshots)
    {
        _snapshots = snapshots;
    }

    public Task SetInterceptorAsync(ISnapshotStoreInterceptor interceptor)
    {
        return _snapshots.Ask<TestSnapshotStore.Ack>(
            new TestSnapshotStore.UseDeleteInterceptor(interceptor),
            TimeSpan.FromSeconds(3)
        );
    }
}