//-----------------------------------------------------------------------
// <copyright file="PersistenceTestKit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit
{
    using Actor;
    using Akka.TestKit;
    using Akka.TestKit.Xunit;
    using Configuration;
    using System;

    public abstract class PersistenceTestKit : TestKitBase
    {
        protected PersistenceTestKit(string actorSystemName = null, string testActorName = null)
            : base(new XunitAssertions(), GetConfig(), actorSystemName, testActorName)
        {
            JournalActorRef = GetJournalRef(Sys);
            Journal = TestJournal.FromRef(JournalActorRef);
        }

        public IActorRef JournalActorRef { get; }

        public ITestJournal Journal { get; }

        public void WithFailingJournalRecovery(Action execution)
        {
            try
            {
                Journal.OnRecovery.Fail();
                execution();
            }
            finally
            {
                // restore normal functionality
                Journal.OnRecovery.Pass();
            }
        }

        public void WithFailingJournalWrites(Action execution)
        {
            try
            {
                Journal.OnWrite.Fail();
                execution();
            }
            finally
            {
                // restore normal functionality
                Journal.OnWrite.Pass();
            }
        }

        static IActorRef GetJournalRef(ActorSystem sys)
            => Persistence.Instance.Apply(sys).JournalFor(null);

        static Config GetConfig()
            => ConfigurationFactory.FromResource<PersistenceTestKit>("Akka.Persistence.TestKit.test-journal.conf");

    }
}