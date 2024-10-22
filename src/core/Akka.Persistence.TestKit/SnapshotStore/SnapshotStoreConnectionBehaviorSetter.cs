//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreRecoveryBehaviorSetter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.TestKit;

/// <summary>
/// Setter strategy for TestSnapshotStore which will set recovery interceptor.
/// </summary>
internal class SnapshotStoreConnectionBehaviorSetter : ISnapshotStoreConnectionBehaviorSetter
{
    internal SnapshotStoreConnectionBehaviorSetter(IActorRef journal)
    {
        _journal = journal;
    }

    private readonly IActorRef _journal;

    public Task SetInterceptorAsync(IConnectionInterceptor interceptor)
        => _journal.Ask<TestSnapshotStore.Ack>(
            new TestSnapshotStore.UseConnectionInterceptor(interceptor),
            TimeSpan.FromSeconds(3)
        );
}