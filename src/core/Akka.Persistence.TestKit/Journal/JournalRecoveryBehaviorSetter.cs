// -----------------------------------------------------------------------
//  <copyright file="JournalRecoveryBehaviorSetter.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.TestKit;

/// <summary>
///     Setter strategy for TestJournal which will set recovery interceptor.
/// </summary>
internal class JournalRecoveryBehaviorSetter : IJournalBehaviorSetter
{
    private readonly IActorRef _journal;

    internal JournalRecoveryBehaviorSetter(IActorRef journal)
    {
        _journal = journal;
    }

    public Task SetInterceptorAsync(IJournalInterceptor interceptor)
    {
        return _journal.Ask<TestJournal.Ack>(
            new TestJournal.UseRecoveryInterceptor(interceptor),
            TimeSpan.FromSeconds(3)
        );
    }
}