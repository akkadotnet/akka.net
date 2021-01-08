//-----------------------------------------------------------------------
// <copyright file="JournalWriteBehaviorSetter.cs" company="Akka.NET Project">
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
    ///     Setter strategy for <see cref="TestJournal"/> which will set write interceptor.
    /// </summary>
    internal class JournalWriteBehaviorSetter : IJournalBehaviorSetter
    {
        internal JournalWriteBehaviorSetter(IActorRef journal)
        {
            this._journal = journal;
        }

        private readonly IActorRef _journal;

        public Task SetInterceptorAsync(IJournalInterceptor interceptor)
            => _journal.Ask<TestJournal.Ack>(
                new TestJournal.UseWriteInterceptor(interceptor),
                TimeSpan.FromSeconds(3)
            );
    }
}
