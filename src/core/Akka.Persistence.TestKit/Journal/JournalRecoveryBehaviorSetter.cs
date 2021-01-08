//-----------------------------------------------------------------------
// <copyright file="JournalRecoveryBehaviorSetter.cs" company="Akka.NET Project">
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
    /// Setter strategy for TestJournal which will set recovery interceptor.
    /// </summary>
    internal class JournalRecoveryBehaviorSetter : IJournalBehaviorSetter
    {
        internal JournalRecoveryBehaviorSetter(IActorRef journal)
        {
            this._journal = journal;
        }

        private readonly IActorRef _journal;

        public Task SetInterceptorAsync(IJournalInterceptor interceptor)
            => _journal.Ask<TestJournal.Ack>(
                new TestJournal.UseRecoveryInterceptor(interceptor),
                TimeSpan.FromSeconds(3)
            );
    }
}
