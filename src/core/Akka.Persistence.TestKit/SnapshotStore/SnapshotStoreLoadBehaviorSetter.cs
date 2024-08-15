// -----------------------------------------------------------------------
//  <copyright file="SnapshotStoreLoadBehaviorSetter.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.TestKit;

/// <summary>
///     Setter strategy for <see cref="TestSnapshotStore" /> which will set load interceptor.
/// </summary>
internal class SnapshotStoreLoadBehaviorSetter : ISnapshotStoreBehaviorSetter
{
    private readonly IActorRef _snapshots;

    internal SnapshotStoreLoadBehaviorSetter(IActorRef snapshots)
    {
        _snapshots = snapshots;
    }

    public Task SetInterceptorAsync(ISnapshotStoreInterceptor interceptor)
    {
        return _snapshots.Ask<TestSnapshotStore.Ack>(
            new TestSnapshotStore.UseLoadInterceptor(interceptor),
            TimeSpan.FromSeconds(3)
        );
    }
}