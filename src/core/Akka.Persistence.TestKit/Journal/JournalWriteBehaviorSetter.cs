// -----------------------------------------------------------------------
//  <copyright file="JournalWriteBehaviorSetter.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.TestKit;

/// <summary>
///     Setter strategy for <see cref="TestJournal" /> which will set write interceptor.
/// </summary>
internal class JournalWriteBehaviorSetter : IJournalBehaviorSetter
{
    private readonly IActorRef _journal;

    internal JournalWriteBehaviorSetter(IActorRef journal)
    {
        _journal = journal;
    }

    public Task SetInterceptorAsync(IJournalInterceptor interceptor)
    {
        return _journal.Ask<TestJournal.Ack>(
            new TestJournal.UseWriteInterceptor(interceptor),
            TimeSpan.FromSeconds(3)
        );
    }
}