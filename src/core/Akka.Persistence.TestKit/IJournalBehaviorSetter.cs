//-----------------------------------------------------------------------
// <copyright file="IJournalBehaviorSetter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using System;
    using Actor;

    public interface IJournalBehaviorSetter
    {
        void SetInterceptor(IJournalInterceptor interceptor);
    }

    internal class JournalWriteBehaviorSetter : IJournalBehaviorSetter
    {
        internal JournalWriteBehaviorSetter(IActorRef journal)
        {
            this._journal = journal;
        }

        private readonly IActorRef _journal;

        public void SetInterceptor(IJournalInterceptor interceptor)
            =>  _journal.Ask(
                new TestJournal.UseWriteInterceptor(interceptor),
                TimeSpan.FromSeconds(3)
            ).Wait();
    }

    internal class JournalRecoveryBehaviorSetter : IJournalBehaviorSetter
    {
        internal JournalRecoveryBehaviorSetter(IActorRef journal)
        {
            this._journal = journal;
        }

        private readonly IActorRef _journal;

        public void SetInterceptor(IJournalInterceptor interceptor)
            =>  _journal.Ask(
                new TestJournal.UseRecoveryInterceptor(interceptor),
                TimeSpan.FromSeconds(3)
            ).Wait();
    }
}