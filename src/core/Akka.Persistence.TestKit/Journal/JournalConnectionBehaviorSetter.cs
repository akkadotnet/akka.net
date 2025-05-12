//-----------------------------------------------------------------------
// <copyright file="JournalConnectionBehaviorSetter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.TestKit;

/// <summary>
/// Setter strategy for TestJournal which will set recovery interceptor.
/// </summary>
internal class JournalConnectionBehaviorSetter : IJournalConnectionBehaviorSetter
{
    internal JournalConnectionBehaviorSetter(IActorRef journal)
    {
        _journal = journal;
    }

    private readonly IActorRef _journal;

    public Task SetInterceptorAsync(IConnectionInterceptor interceptor)
        => _journal.Ask<TestJournal.Ack>(
            new TestJournal.UseConnectionInterceptor(interceptor),
            TimeSpan.FromSeconds(3)
        );
}
